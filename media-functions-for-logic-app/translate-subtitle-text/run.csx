#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#load "../Shared/CopyBlobHelpers.csx"

using System;
using System.Threading;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using System.Text.RegularExpressions;
using System.Collections.Generic;


static string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
static string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");
static string TranslatorKey = Environment.GetEnvironmentVariable("TranslatorKey");

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"Webhook was triggered!");

    string jsonContent = await req.Content.ReadAsStringAsync();
    dynamic data = JsonConvert.DeserializeObject(jsonContent);
    string inCon = data.container;
    string assetName = data.assetName;
    string language = data.language;
    Dictionary<string, string> output;
    if (data.assetName == null ||  data.container == null || data.language == null)
    {
        
        return req.CreateResponse(HttpStatusCode.BadRequest, new
        {
            error = "Please pass assetName, container and language in the input object"
        });
    }
    List<string> utterances = new List<string>();
    Regex g = new Regex(@"\d{2}:\d{2}:\d{2}.\d{3}");
    try{
        log.Info($"assetName = {assetName} and container={inCon}");
        CloudStorageAccount sourceStorageAccount = new CloudStorageAccount(new StorageCredentials(_storageAccountName, _storageAccountKey), true);
        CloudBlobClient sourceCloudBlobClient = sourceStorageAccount.CreateCloudBlobClient();
        CloudBlobContainer source =  sourceCloudBlobClient.GetContainerReference(inCon);
        CloudBlockBlob blob = source.GetBlockBlobReference(assetName);
        using (var stream = blob.OpenRead())
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                while (!reader.EndOfStream)
                {
                    line = reader.ReadLine();
                    //log.Info(line);
                    Match m = g.Match(line);
                    if (m.Success)
                    {
                        // Y.
                        // Write original line and the value.
                        string v = m.Groups[1].Value;
                        log.Info($"Matched - {line}");
                        
                    }
                    else 
                    {
                        if(line.Trim().Length > 0)
                        {
                            utterances.Add(line);
                        }
                    }
                }
            }
            string[] utArr = utterances.ToArray();
            string authToken = await GetTranslateAuthToken(TranslatorKey, log);
            output = await TranslateArray(utArr, language, authToken, log);
            log.Info($"{output[utArr[1]]}"); 
        }
        string newBlob, value;
        int pos = assetName.IndexOf(".vtt");
        if(pos > 0)
        {
            newBlob = string.Format("{0}{1}.vtt", assetName.Substring(0,pos), language);
            //Now need to write to a new blob
            CloudAppendBlob appBlob =source.GetAppendBlobReference(newBlob);
            appBlob.CreateOrReplace();
            
            using (var stream = blob.OpenRead())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    
                    string line;
                    while (!reader.EndOfStream)
                    {
                        
                        line = reader.ReadLine();
                        if (output.TryGetValue(line, out value))
                        {
                            appBlob.AppendText(value);
                        }
                        else
                        {
                            appBlob.AppendText(line);
                        }
                    }
                }
            }
            
 
            
        }

            
        
 
            
    }
    catch(Exception ex)
    {
        log.Info($"Exception - {ex.Message}");
        log.Info($"{ex.StackTrace}");
        throw;
    }
    return req.CreateResponse(HttpStatusCode.OK, new {
        greeting = $"Hello {data.assetName} translated to {data.language}!"
    });
}
