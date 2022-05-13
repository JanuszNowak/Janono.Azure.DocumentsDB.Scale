namespace Janono.Azure.DocumentsDB.Scale
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Azure.Documents.Linq;

    public static class CosmosDbHelper
    {

        public static void DecreaseRUs(DocumentClient client, string databaseId, string collectionId,
            bool sharedOfferThroughput = false, int rUsOfferThroughputMin = 400, int rUsScaleStepDown = 100)
        {
            Uri dataBaseOrCollectionLink;
            string selfLink;

            if (sharedOfferThroughput)
            {
                dataBaseOrCollectionLink = UriFactory.CreateDatabaseUri(databaseId);
                var resourceResponse = client.ReadDatabaseAsync(dataBaseOrCollectionLink).Result;
                var dbOrCollectionResource = resourceResponse.Resource;
                selfLink = dbOrCollectionResource.SelfLink;
            }
            else
            {
                dataBaseOrCollectionLink = UriFactory.CreateDocumentCollectionUri(databaseId, collectionId);
                var resourceResponse = client.ReadDocumentCollectionAsync(dataBaseOrCollectionLink).Result;
                var dbOrCollectionResource = resourceResponse.Resource;
                selfLink = dbOrCollectionResource.SelfLink;
            }

            var offer = client.CreateOfferQuery()
                .Where(o => o.ResourceLink == selfLink)
                .AsDocumentQuery()
                .ExecuteNextAsync<OfferV2>().Result.FirstOrDefault();

            if (offer != null)
            {
                var currentOfferThroughput = offer.Content.OfferThroughput;
                Trace.WriteLine($"currentOfferThroughput: {currentOfferThroughput} ");

                if (currentOfferThroughput > rUsOfferThroughputMin)
                {
                    var start = DateTime.Now;
                    var newOfferThroughput = currentOfferThroughput - rUsScaleStepDown;
                    var updatedOffer = new OfferV2(offer, offerThroughput: newOfferThroughput);
                    var offerAfterUpdate = (OfferV2)client.ReplaceOfferAsync(updatedOffer).Result.Resource;
                    var end = DateTime.Now;
                    var span = end - start;

                    Trace.WriteLine($"OldOfferThroughput: {currentOfferThroughput}   NewOfferThroughput: {offerAfterUpdate.Content.OfferThroughput} take {span} ms");
                }
            }
        }

        public static void IncreaseRUs(DocumentClient client, string databaseId, string collectionId,
            bool sharedOfferThroughput = false, int rUsOfferThroughputMax = 1000, int rUsScaleStepUp = 200, TimeSpan? retryAfter = null)
        {

            try
            {
                Uri dataBaseOrCollectionLink;
                string selfLink;

                if (sharedOfferThroughput)
                {
                    dataBaseOrCollectionLink = UriFactory.CreateDatabaseUri(databaseId);
                    var resourceResponse = client.ReadDatabaseAsync(dataBaseOrCollectionLink).Result;
                    var dbOrCollectionResource = resourceResponse.Resource;
                    selfLink = dbOrCollectionResource.SelfLink;
                }
                else
                {
                    dataBaseOrCollectionLink = UriFactory.CreateDocumentCollectionUri(databaseId, collectionId);
                    var resourceResponse = client.ReadDocumentCollectionAsync(dataBaseOrCollectionLink).Result;
                    var dbOrCollectionResource = resourceResponse.Resource;
                    selfLink = dbOrCollectionResource.SelfLink;
                }

                var offer = client.CreateOfferQuery()
                    .Where(o => o.ResourceLink == selfLink)
                    .AsDocumentQuery()
                    .ExecuteNextAsync<OfferV2>().Result.FirstOrDefault();

                if (offer != null)
                {
                    var currentOfferThroughput = offer.Content.OfferThroughput;

                    Trace.WriteLine($"currentOfferThroughput: {currentOfferThroughput} ");

                    if (currentOfferThroughput < rUsOfferThroughputMax)
                    {
                        var start = DateTime.Now;
                        var newOfferThroughput = currentOfferThroughput + rUsScaleStepUp;
                        var updatedOffer = new OfferV2(offer, offerThroughput: newOfferThroughput);
                        var offerAfterUpdate = (OfferV2)client.ReplaceOfferAsync(updatedOffer).Result.Resource;
                        var end = DateTime.Now;
                        var span = end - start;

                        Trace.WriteLine($"OldOfferThroughput: {currentOfferThroughput}   NewOfferThroughput: {offerAfterUpdate.Content.OfferThroughput} take {span} ms");
                    }
                    else
                    {
                        if (retryAfter != null)
                        {
                            Trace.WriteLine("RetryAfter: " + retryAfter);
                            Task.Delay((TimeSpan)retryAfter);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }


        public static async Task<T> ExecuteScale<T>(DocumentClient client, Func<DocumentClient, Task<T>> fn,
            string databaseId, string collectionId, bool sharedOfferThroughput = false, int rUsOfferThroughputMin = 400, int rUsOfferThroughputMax = 1000, int counter = 0, int maxTry = 4, int rUsScaleStepDown = 100, int rUsScaleStepUp = 200)
        {
            try
            {
                return await fn(client);
            }
            catch (DocumentClientException dce) when (dce.Message.Contains(SyntaxErrorMessage))
            {
                throw new QuerySyntaxException(dce);
            }
            catch (DocumentClientException dce) when (dce.Message.Contains("Request rate is large"))
            {
                counter++;
                IncreaseRUs(client, databaseId, collectionId, sharedOfferThroughput, rUsOfferThroughputMax, rUsScaleStepUp, dce.RetryAfter);

                if (counter < maxTry)
                {
                    Trace.WriteLine("Retry");
                    return await ExecuteScale(client, fn, databaseId, collectionId, sharedOfferThroughput, rUsOfferThroughputMin, rUsOfferThroughputMax, counter);
                }
                else
                {
                    Trace.WriteLine("Failed retry and scale");
                    throw;
                }
            }
        }

        private const string SyntaxErrorMessage = "Syntax error";
    }

    public class QuerySyntaxException : ArgumentException
    {
        private const string DefaultSyntaxErrorMessage = "The query contains syntax errors.";

        public QuerySyntaxException() : base(DefaultSyntaxErrorMessage)
        {
        }

        public QuerySyntaxException(DocumentClientException dce) : base(TryGetSyntaxErrorMessageFromException(dce))
        {

        }

        public QuerySyntaxException(string message) : base(string.IsNullOrWhiteSpace(message) ? DefaultSyntaxErrorMessage : message)
        {
        }

        private static string TryGetSyntaxErrorMessageFromException(DocumentClientException ex)
        {
            try
            {
                var message = ex.Message;
                if (!message.StartsWith("Message: "))
                    return null;

                message = message.Substring("Message: ".Length);
                message = message.Substring(0, message.LastIndexOf('}') + 1);

                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<SyntaxErrorData>(message);
                return $"Invalid query. {data.GetMessage()}";
            }
            catch
            {
                return DefaultSyntaxErrorMessage;
            }
        }

        public class SyntaxErrorData
        {
            public List<SyntaxError> Errors { get; set; }

            public string GetMessage()
            {
                return string.Join(" ", Errors.Select(o => o.Message));
            }
        }

        public class SyntaxError
        {
            public string Severity { get; set; }

            public SyntaxErrorLocation Location { get; set; }

            public string Code { get; set; }

            public string Message { get; set; }
        }

        public class SyntaxErrorLocation
        {
            public int Start { get; set; }

            public int End { get; set; }
        }
    }
}
