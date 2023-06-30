# SearchGPT

A sample that shows how to integrate ChatGPT with your own data that comes from Azure Cognitive Search.

You need to set the required values in the [appsettings.json](https://github.com/marcominerva/SearchGPT/blob/master/src/SearchGpt/appsettings.json) file:

    "ChatGPT": {
        "Provider": "OpenAI",           // Optional. Allowed values: OpenAI (default) or Azure
        "ApiKey": "",                   // Required
        "Organization": "",             // Optional, used only by OpenAI
        "ResourceName": "",             // Required when using Azure OpenAI Service
        "AuthenticationType": "ApiKey", // Optional, used only by Azure OpenAI Service. Allowed values : ApiKey (default) or ActiveDirectory
        "DefaultModel": "my-model"      // Required  
    },
    "CognitiveSearchSettings": {
        "ServiceName": "",
        "ApiKey": "",
        "IndexName": ""
    }

> **Note**
The search index must contain a field named `content` that is searchable and retrievable. If you want to use different fields, you need to change the code in the [ChatService.cs](https://github.com/marcominerva/SearchGPT/blob/master/src/SearchGpt.BusinessLayer/Services/ChatService.cs) class.

For more information about the structure of the project, refer to [ChatGptPlayground documentation](https://github.com/marcominerva/ChatGptPlayground).