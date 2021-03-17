/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Linq;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;
using System.Security.Cryptography;
using System.Text;


namespace forgeSample.Controllers
{
    [ApiController]
    public class DesignAutomationController : ControllerBase
    {
        // Used to access the application folder (temp location for files & bundles)
        private IWebHostEnvironment _env;
        // used to access the SignalR Hub
        private IHubContext<DesignAutomationHub> _hubContext;
        // Local folder for bundles
        public string LocalBundlesFolder { get { return Path.Combine(_env.WebRootPath, "bundles"); } }
        public string LocalFilesFolder { get { return Path.Combine(_env.WebRootPath, "files"); } }

        public string WorkflowId { get { return "my-workflow-id"; } }
        /// Prefix for AppBundles and Activities
        public static string NickName { get { return OAuthController.GetAppSetting("FORGE_CLIENT_ID"); } }
        public static string TransientBucketKey { get { return NickName.ToLower() + "-transient"; } }

        public static string PersistentBucketKey { get { return NickName.ToLower() + "-persistent"; } }

        public static string DefaultFileName { get { return "Engine MKII.iam.zip"; } }

        private const int UPLOAD_CHUNK_SIZE = 2 * 1024 * 1024; // 2 Mb

        public static string QualifiedBundleActivityName { get { return string.Format("{0}.{1}+{2}", NickName, kBundleActivityName, Alias); } }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "prod"; } }
        // Design Automation v3 API
        DesignAutomationClient _designAutomation;

        public const string kEngineName = "Autodesk.Inventor+2021";
        public const string kBundleActivityName = "SimplifyModel";
        //public const string kOutputFileName = "shelves.iam.zip";

        // Constructor, where env and hubContext are specified
        public DesignAutomationController(IWebHostEnvironment env, IHubContext<DesignAutomationHub> hubContext, DesignAutomationClient api)
        {
            _designAutomation = api;
            _env = env;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Get all Activities defined for this account
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/defaultmodel")] 
        public IActionResult GetDefaultModel()
        {
            System.Diagnostics.Debug.WriteLine("GetDefaultModel");
            // filter list of 
            string urn = string.Format("urn:adsk.objects:os.object:{0}/{1}", PersistentBucketKey, Uri.EscapeUriString(DefaultFileName));

            return Ok(new { Urn = Base64Encode(urn) });
        }

        /// <summary>
        /// Helps identify the engine
        /// </summary>
        private string CommandLine()
        {
            return $"$(engine.path)\\InventorCoreConsole.exe /al \"$(appbundles[{kBundleActivityName}].path)\"";
        }

        /// <summary>
        /// Base64 enconde a string
        /// </summary>
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes).Replace("=", "");
        }

        /// <summary>
        /// Base64 deconde a string
        /// </summary>
        public static string Base64Decode(string base64Encoded)
        {
            base64Encoded = base64Encoded.PadRight(base64Encoded.Length + (4 - base64Encoded.Length % 4) % 4, '=');
            byte[] data = System.Convert.FromBase64String(base64Encoded);
            return System.Text.Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Upload sample files
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/files")]
        public async Task<IActionResult> UploadOssFiles([FromBody]JObject appBundleSpecs)
        {
            if (OAuthController.GetAppSetting("DISABLE_SETUP") == "true")
            {
                return Unauthorized();
            }

            System.Diagnostics.Debug.WriteLine("UploadOssFiles");
            // OAuth token
            dynamic oauth = await OAuthController.GetInternalAsync();

            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = oauth.access_token;

            DerivativesApi derivatives = new DerivativesApi();
            derivatives.Configuration.AccessToken = oauth.access_token;

            // upload file to OSS Bucket
            // 1. ensure bucket existis
            BucketsApi buckets = new BucketsApi();
            buckets.Configuration.AccessToken = oauth.access_token;
            try
            {
                PostBucketsPayload bucketPayload = new PostBucketsPayload(PersistentBucketKey, null, PostBucketsPayload.PolicyKeyEnum.Persistent);
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }; // in case bucket already exists
            try
            {
                PostBucketsPayload bucketPayload = new PostBucketsPayload(TransientBucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }; // in case bucket already exists

            string [] filePaths = System.IO.Directory.GetFiles(LocalFilesFolder);
            foreach (string filePath in filePaths)
            {
                string fileName = System.IO.Path.GetFileName(filePath);
                string objectId = await UploadFile(PersistentBucketKey, filePath);

                System.Diagnostics.Debug.WriteLine("Translating " + fileName);
                _ = TranslateFile(objectId, fileName.Replace(".zip", ""));
            } 

            DerivativeWebhooksApi webhooks = new DerivativeWebhooksApi(); 
            webhooks.Configuration.AccessToken = oauth.access_token;

            dynamic webhookRes = await webhooks.GetHooksAsync(DerivativeWebhookEvent.ExtractionFinished);
            foreach (KeyValuePair<string, dynamic> item in new DynamicDictionaryItems(webhookRes.data)) 
            {
                Guid hookId = new Guid(item.Value.hookId);
                System.Diagnostics.Debug.WriteLine("Deleting webhook, hookId " + hookId);
                await webhooks.DeleteHookAsync(DerivativeWebhookEvent.ExtractionFinished, hookId);
            }
           
            string callbackComplete = string.Format(
                "{0}/api/forge/callback/ontranslated", 
                OAuthController.GetAppSetting("FORGE_WEBHOOK_URL")
            );
            
            System.Diagnostics.Debug.WriteLine("Creating webhook with workflowId = " +  WorkflowId);
            dynamic res = await webhooks.CreateHookAsync(DerivativeWebhookEvent.ExtractionFinished, callbackComplete, WorkflowId);
            System.Diagnostics.Debug.WriteLine("Created webhook");

            return Ok();
        }

        private async Task<string> UploadFile(string bucketKey, string filePath)
        {
             // OAuth token
            dynamic oauth = await OAuthController.GetInternalAsync();
            string objectId = "";

            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = oauth.access_token;

            string fileName = System.IO.Path.GetFileName(filePath);
            using (BinaryReader binaryReader = new BinaryReader(new FileStream(filePath, FileMode.Open)))
            {
                System.Diagnostics.Debug.WriteLine("Uploading " + fileName);
                //dynamic uploadRes = await objects.UploadObjectAsync(PersistentBucketKey, fileName, (int)streamReader.BaseStream.Length, streamReader.BaseStream, "application/octet-stream");
                // get file size
                long fileSize = binaryReader.BaseStream.Length;

                // decide if upload direct or resumable (by chunks)
                if (fileSize > UPLOAD_CHUNK_SIZE) // upload in chunks
                {
                    long chunkSize = UPLOAD_CHUNK_SIZE; // 2 Mb
                    long numberOfChunks = (long)Math.Round((double)(fileSize / chunkSize)) + 1;
                    long start = 0;
                    chunkSize = (numberOfChunks > 1 ? chunkSize : fileSize);
                    long end = chunkSize;
                    string sessionId = Guid.NewGuid().ToString();

                    // upload one chunk at a time
                    for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
                    {
                        string range = string.Format("bytes {0}-{1}/{2}", start, end, fileSize);

                        long numberOfBytes = chunkSize + 1;
                        byte[] fileBytes = new byte[numberOfBytes];
                        MemoryStream memoryStream = new MemoryStream(fileBytes);
                        binaryReader.BaseStream.Seek((int)start, SeekOrigin.Begin);
                        int count = binaryReader.Read(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Write(fileBytes, 0, (int)numberOfBytes);
                        memoryStream.Position = 0;

                        dynamic chunkUploadResponse = await objects.UploadChunkAsyncWithHttpInfo(bucketKey, fileName, (int)numberOfBytes, range, sessionId, memoryStream);
                        if (chunkUploadResponse.StatusCode == 200) 
                        {
                            objectId = chunkUploadResponse.Data.objectId;
                        }

                        start = end + 1;
                        chunkSize = ((start + chunkSize > fileSize) ? fileSize - start - 1 : chunkSize);
                        end = start + chunkSize;
                    }
                }
                else // upload in a single call
                {
                    using (StreamReader streamReader = new StreamReader(filePath))
                    {
                        dynamic uploadedObj = await objects.UploadObjectAsync(
                            bucketKey,
                            fileName, 
                            (int)streamReader.BaseStream.Length, 
                            streamReader.BaseStream,
                            "application/octet-stream");

                        objectId = uploadedObj.objectId;
                    }
                }
            }

            return objectId;
        }

        private async Task TranslateFile(string objectId, string rootFileName, string workflowId = null)
        {
            dynamic oauth = await OAuthController.GetInternalAsync();

            // prepare the payload
            List<JobPayloadItem> outputs = new List<JobPayloadItem>()
            {
                new JobPayloadItem(
                    JobPayloadItem.TypeEnum.Svf,
                    new List<JobPayloadItem.ViewsEnum>()
                    {
                        JobPayloadItem.ViewsEnum._2d,
                        JobPayloadItem.ViewsEnum._3d
                    }
                )
            };
            JobPayloadMisc misc = null;
            if (workflowId != null)
            {
                misc = new JobPayloadMisc(workflowId);
            }

            JobPayload job;
            string urn = Base64Encode(objectId);
            if (rootFileName != null)
            {
                job = new JobPayload(new JobPayloadInput(urn, true, rootFileName), new JobPayloadOutput(outputs), misc);
            }
            else
            {
                job = new JobPayload(new JobPayloadInput(urn), new JobPayloadOutput(outputs));
            }

            // start the translation
            DerivativesApi derivative = new DerivativesApi();
            derivative.Configuration.AccessToken = oauth.access_token;

            dynamic res = await derivative.TranslateAsync(job, true);
        }

        /// <summary>
        /// Define a new appbundle
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/appbundles")]
        public async Task<IActionResult> CreateAppBundle([FromBody]JObject appBundleSpecs)
        {
            if (OAuthController.GetAppSetting("DISABLE_SETUP") == "true")
            {
                return Unauthorized();
            }

            System.Diagnostics.Debug.WriteLine("CreateAppBundle");
            string zipFileName = "ShrinkWrapPlugin.bundle";

            // check if ZIP with bundle is here
            string packageZipPath = Path.Combine(LocalBundlesFolder, zipFileName + ".zip");
            if (!System.IO.File.Exists(packageZipPath)) throw new Exception("Appbundle not found at " + packageZipPath);

            // get defined app bundles
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();

            // check if app bundle is already define
            dynamic newAppVersion;
            string qualifiedAppBundleId = string.Format("{0}.{1}+{2}", NickName, kBundleActivityName, Alias);
            if (!appBundles.Data.Contains(qualifiedAppBundleId))
            {
                // create an appbundle (version 1)
                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = kBundleActivityName,
                    Engine = kEngineName,
                    Id = kBundleActivityName,
                    Description = string.Format("Description for {0}", kBundleActivityName),

                };
                newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");

                // create alias pointing to v1
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(kBundleActivityName, aliasSpec);
            }
            else
            {
                // create new version
                AppBundle appBundleSpec = new AppBundle()
                {
                    Engine = kEngineName,
                    Description = kBundleActivityName
                };
                newAppVersion = await _designAutomation.CreateAppBundleVersionAsync(kBundleActivityName, appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new version");

                // update alias pointing to v+1
                AliasPatch aliasSpec = new AliasPatch()
                {
                    Version = newAppVersion.Version
                };
                Alias newAlias = await _designAutomation.ModifyAppBundleAliasAsync(kBundleActivityName, Alias, aliasSpec);
            }

            // upload the zip with .bundle
            RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
            RestRequest request = new RestRequest(string.Empty, Method.POST);
            request.AlwaysMultipartFormData = true;
            foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
            request.AddFile("file", packageZipPath);
            request.AddHeader("Cache-Control", "no-cache");
            await uploadClient.ExecuteTaskAsync(request);

            return Ok(new { AppBundle = QualifiedBundleActivityName, Version = newAppVersion.Version });
        }

        /// <summary>
        /// Define a new activity
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/activities")]
        public async Task<IActionResult> CreateActivity([FromBody]JObject activitySpecs)
        {
            if (OAuthController.GetAppSetting("DISABLE_SETUP") == "true")
            {
                return Unauthorized();
            }

            System.Diagnostics.Debug.WriteLine("CreateActivity");
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            if (!activities.Data.Contains(QualifiedBundleActivityName))
            {
                string commandLine = CommandLine();
                Activity activitySpec = new Activity()
                {
                    Id = kBundleActivityName,
                    Appbundles = new List<string>() { QualifiedBundleActivityName },
                    CommandLine = new List<string>() { commandLine },
                    Engine = kEngineName,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputZip", new Parameter() { Description = "input zip", LocalName = "files", Ondemand = false, Required = true, Verb = Verb.Get, Zip = true } },
                        { "inputJson", new Parameter() { Description = "input json", LocalName = "input.json", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "outputZip", new Parameter() { Description = "output zip file", LocalName = "files", Ondemand = false, Required = false, Verb = Verb.Put, Zip = true } },
                        { "outputRfa", new Parameter() { Description = "output zip file", LocalName = "output.rfa", Ondemand = false, Required = false, Verb = Verb.Put, Zip = false } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(kBundleActivityName, aliasSpec);

                return Ok(new { Activity = QualifiedBundleActivityName });
            }

            // as this activity points to a AppBundle "dev" alias (which points to the last version of the bundle),
            // there is no need to update it (for this sample), but this may be extended for different contexts
            return Ok(new { Activity = "Activity already defined" });
        }

        /// <summary>
        /// Start a new workitem
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/workitems")]
        public async Task<IActionResult> StartWorkitems([FromBody]JObject input)
        {
            System.Diagnostics.Debug.WriteLine("StartWorkitem");
            string browerConnectionId = input["browerConnectionId"].Value<string>();
            bool isDefault = input["isDefault"].Value<bool>();
            if (isDefault)
            {
                input["options"]["MainAssembly"] = new JObject(new JProperty("value", DefaultFileName.Replace(".zip", "")));
            }
            bool createRfa = input["options"]["CreateRfa"]["value"].Value<bool>();

            // OAuth token
            dynamic oauth = await OAuthController.GetInternalAsync();

            string inputBucket = isDefault ? PersistentBucketKey : TransientBucketKey;
            string inputZip = isDefault ? DefaultFileName : browerConnectionId + ".zip";
            string outputZip = browerConnectionId + ".min.zip";
            string outputRfa = createRfa ? browerConnectionId + ".rfa" : null;
            string zipWorkItemId = await CreateWorkItem(
                input["options"],
                new Dictionary<string, string>() { { "Authorization", "Bearer " + oauth.access_token } },
                browerConnectionId,
                inputBucket,
                inputZip,
                outputZip,
                outputRfa
            );

            return Ok(new {
                ZipWorkItemId = zipWorkItemId
            });
        }
        private async Task<string> CreateWorkItem(JToken input, Dictionary<string, string> headers, string browerConnectionId, string inputBucket, string inputName, string outputName, string outputRfaName)
        {
            input["output"] = outputName;
            XrefTreeArgument inputJsonArgument = new XrefTreeArgument()
            {
                Url = "data:application/json," + input.ToString(Formatting.None)
            };

            string inputUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", inputBucket, inputName);
            string outputUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", TransientBucketKey, outputName);
            string outputRfaUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", TransientBucketKey, outputRfaName);

            XrefTreeArgument inputArgument = new XrefTreeArgument()
            {
                Url = inputUrl,
                Verb = Verb.Get,
                Headers = headers
            };

            XrefTreeArgument outputArgument = new XrefTreeArgument()
            {
                Url = outputUrl,
                Verb = Verb.Put,
                Headers = headers
            };

            XrefTreeArgument outputRfaArgument = new XrefTreeArgument()
            {
                Url = outputRfaUrl,
                Verb = Verb.Put,
                Headers = headers
            };

            string callbackComplete = string.Format(
                "{0}/api/forge/callback/oncomplete?id={1}&outputFile={2}&outputRfaFile={3}", 
                OAuthController.GetAppSetting("FORGE_WEBHOOK_URL"), 
                browerConnectionId, 
                outputName,
                outputRfaName);

            XrefTreeArgument callbackArgument = new XrefTreeArgument { 
                Verb = Verb.Post, 
                Url = callbackComplete 
            };

            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = QualifiedBundleActivityName,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "inputZip", inputArgument },        
                    { "inputJson", inputJsonArgument },
                    { "outputZip", outputArgument },
                    { "onComplete", callbackArgument }
                }
            };

            if (outputRfaName != null)
            {
                workItemSpec.Arguments.Add("outputRfa", outputRfaArgument);
            }

            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemAsync(workItemSpec);

            return workItemStatus.Id;
        }

        /// <summary>
        /// Start a new workitem
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/translations")]
        public async Task<IActionResult> StartTranslation([FromBody]JObject input)
        {
            string browerConnectionId = input["browerConnectionId"].Value<string>();
            bool isSimplified = input["isSimplified"].Value<bool>();
            string rootFilename = isSimplified ? "output.ipt" : input["rootFilename"].Value<string>();

            // OAuth token
            dynamic oauth = await OAuthController.GetInternalAsync();

            string zipFileName = browerConnectionId + ((isSimplified) ? ".min.zip" : ".zip");

            try
            {
                await TranslateFile(
                    string.Format("urn:adsk.objects:os.object:{0}/{1}", TransientBucketKey, zipFileName),
                    rootFilename,
                    WorkflowId
                );
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
           

            return Ok();
        }

        /// <summary>
        /// Callback from Design Automation Workitem (onProgress or onComplete)
        /// </summary>
        [HttpPost]
        [Route("/api/forge/callback/oncomplete")]
        public async Task<IActionResult> OnComplete(string id, string outputFile, string outputRfaFile, [FromBody]dynamic body)
        {
            System.Diagnostics.Debug.WriteLine($"OnComplete, id = {id}, outputFile = {outputFile}, outputRfaFile = {outputRfaFile}");
            try
            {
                JObject bodyJson = JObject.Parse((string)body.ToString());
                // your webhook should return immediately! we can use Hangfire to schedule a job
                var client = new RestClient(bodyJson["reportUrl"].Value<string>());
                var request = new RestRequest(string.Empty);

                byte[] bs = client.DownloadData(request);
                string report = System.Text.Encoding.Default.GetString(bs);
                await _hubContext.Clients.Client(id).SendAsync("onReport", report);
               
                await _hubContext.Clients.Client(id).SendAsync("onComplete", bodyJson.ToString());

                // If we got a reasonable file name
                if (outputRfaFile != null) 
                {
                    dynamic oauth = await OAuthController.GetInternalAsync();

                    ObjectsApi objects = new ObjectsApi();
                    objects.Configuration.AccessToken = oauth.access_token;

                    dynamic signedUrl = await objects.CreateSignedResourceAsyncWithHttpInfo(TransientBucketKey, outputRfaFile, new PostBucketsSigned(30), "read");
                    
                    string url = signedUrl.Data.signedUrl;
                    await _hubContext.Clients.Client(id).SendAsync("onUrl", "{ \"url\": \"" + url + "\", \"text\": \"Download output.rfa\" }");
                }
            }
            catch (Exception e) 
            {
                System.Diagnostics.Debug.WriteLine("OnComplete, e.Message = " + e.Message);
            }

            // ALWAYS return ok (200)
            return Ok();
        }

        /// <summary>
        /// Callback from Design Automation Workitem (onProgress or onComplete)
        /// </summary>
        [HttpPost]
        [Route("/api/forge/callback/ontranslated")]
        public async Task<IActionResult> OnTranslated([FromBody]dynamic body)
        {
            System.Diagnostics.Debug.WriteLine("OnTranslated");
            try
            {
                string urn = body.resourceUrn;
                System.Diagnostics.Debug.WriteLine("OnTranslated, urn = " + urn);
                string fileName = Base64Decode(urn).Split("/")[1];
                bool isSimplified = fileName.EndsWith(".min.zip");
                string id = fileName.Replace(".zip", "").Replace(".min", "");
                string status = body.payload.Payload.status;

                // your webhook should return immediately! we can use Hangfire to schedule a job
                await _hubContext.Clients.Client(id).SendAsync(
                    "onTranslated",  
                    $"{{ \"urn\": \"{urn}\", \"isSimplified\": {isSimplified.ToString().ToLower()}, \"status\": \"{status}\" }}"
                );
            }
            catch (Exception e) 
            {
                System.Diagnostics.Debug.WriteLine("OnTranslated, e.Message = " + e.Message);
            }

            // ALWAYS return ok (200)
            return Ok();
        }

        /// <summary>
        /// Clear the accounts (for debugging purposes)
        /// </summary>
        [HttpDelete]
        [Route("api/forge/designautomation/account")]
        public async Task<IActionResult> ClearAccount()
        {
           if (OAuthController.GetAppSetting("DISABLE_SETUP") == "true")
           {
               return Unauthorized();
           }

            // clear account
            await _designAutomation.DeleteForgeAppAsync("me");
            return Ok();
        }

        /// <summary>
        /// Clear the accounts (for debugging purposes)
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/uploadurl")]
        public async Task<IActionResult> GetUploadUrl(string id)
        {
            var fileName = id + ".zip";
            dynamic oauth = await OAuthController.GetInternalAsync();
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = oauth.access_token;
            dynamic signedUrl = await objects.CreateSignedResourceAsyncWithHttpInfo(TransientBucketKey, fileName, new PostBucketsSigned(10), "write");
            
            return Ok(new {
                SignedUrl = signedUrl
            });
        }
    }

    /// <summary>
    /// Class uses for SignalR
    /// </summary>
    public class DesignAutomationHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string GetConnectionId() 
        {
            System.Diagnostics.Debug.WriteLine("GetConnectionId, Context.ConnectionId = " + Context.ConnectionId);
            return Context.ConnectionId; 
        }
    }

}