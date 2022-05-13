

# Janono.Azure.DocumentsDB.Scale

[![Build status](https://dev.azure.com/janono-pub/Janono.Azure.DocumentsDB.Scale/_apis/build/status/Janono.Azure.DocumentsDB.Scale-CI)](https://dev.azure.com/janono-pub/Janono.Azure.DocumentsDB.Scale/_build/latest?definitionId=9)

## Janono.Azure.DocumentsDB.Scale

 Is still under active development at https://dev.azure.com/janono-pub/Janono.Azure.DocumentsDB.Scale.

## Janono.Azure.DocumentsDB.Scale?

Janono.Azure.DocumentsDB.Scale is implementation of Auto Scaling Azure Cosmos DB, because of lacking build in auto scaling.

Package name                              | Stable
------------------------------------------|-------------------------------------------
`Janono.Azure.DocumentsDB.Scale`          | [![NuGet](https://img.shields.io/nuget/v/Janono.Azure.DocumentsDB.Scale.svg?style=flat-square&label=nuget)](https://www.nuget.org/packages/Janono.Azure.DocumentsDB.Scale/)



## Example of usage Janono.Azure.DocumentsDB.Scale

```
using Janono.Azure.DocumentsDB.Scale;

ConnectionPolicy connectionPolicy = new ConnectionPolicy
{
    //RetryOptions = { MaxRetryAttemptsOnThrottledRequests = 9, MaxRetryWaitTimeInSeconds = 30 }
    //changing defaults to not receive 429 after 30 seconds and make instant scaling more effective
    RetryOptions = { MaxRetryAttemptsOnThrottledRequests = 2, MaxRetryWaitTimeInSeconds = 3 }
};

var _collectionLink = UriFactory.CreateDocumentCollectionUri(ConData.DbName, ConData.CollectionLink);

using (var client = new DocumentClient(new Uri(ConData.CosmosDbEndpoint), ConData.CosmosDbKey, connectionPolicy))
{
    //with no auto scaling
    client.CreateDocumentAsync(_collectionLink, documentToInsert);

    //with auto scaling
    CosmosDbHelper.ExecuteScale(client, c => c.CreateDocumentAsync(_collectionLink, documentToInsert),
    ConData.DbName,
    ConData.CollectionClick,
    sharedOfferThroughput: true, rUsOfferThroughputMin: 400, rUsOfferThroughputMax: 20000, rUsScaleStepUp: 300, maxTry: 8).GetAwaiter().GetResult();
}
```

```
using System;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Janono.Azure.DocumentsDB.Scale;

public static class ScaleDown
{
    [FunctionName("ScaleDown")]
    public static void Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
    {
        log.LogInformation($"Scale down: {DateTime.Now}");

        using (var client = new DocumentClient(new Uri(ConData.CosmosDbEndpoint), ConData.CosmosDbKey))
        {
            CosmosDbHelper.DecreaseRUs(client, ConData.DbName, ConData.CollectionName, , sharedOfferThroughput:true, rUsOfferThroughputMin: 400, rUsScaleStepDown: 200);
        }
    }
}
```