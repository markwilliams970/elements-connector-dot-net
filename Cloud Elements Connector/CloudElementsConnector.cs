﻿using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;


namespace Cloud_Elements_API
{
    public class CloudElementsConnector
    {
        #region "privates"
        static readonly string DefaultElementsPublicURL = "https://api.cloud-elements.com/elements/api-v2/";
        private readonly static string StaticLockObject = "";
        // note: InstanceLockObject is APIClient;
        private static int ConnectorInstanceCounter = 0;
        private static int HttpClientInstanceCounter = 0;
        private static int RequestCounter = 0;
        private static double TotalRequestMS = 0d;
        private int ConnectorInstanceNumber = 0;
        private int InstanceRequestCounter = 0;
        private double InstanceTotalRequestMS = 0d;

        // ...hubs/documents/folders/contents?path=%2FSQL&fetchTags=true"
        // ...hubs/documents/files/21794645297/metadata
        private Cloud_Elements_API.CloudAuthorization AuthorizationData;
        private HttpClient APIClient;
        private DateTime InstanceCreated;
        private string LastFailureInformation = "";
        #endregion

        public static bool WriteDiagTrace = true;
        public static bool SimplifyLoggedURIs = true;
        public static TraceLevel DiagOutputLevel = TraceLevel.NonSuccess;
        public delegate void DiagTraceEventHanlder(object sender, string info);
        public event DiagTraceEventHanlder DiagTrace;

        public string ElementsPublicUrl { get; set; }
        public Cloud_Elements_API.CloudAuthorization APIAuthorization
        {
            set
            {
                AuthorizationData = value;
                if (value == null)
                {
                    APIClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("basic", "empty");
                }
                else APIClient.DefaultRequestHeaders.Authorization = AuthorizationData.GetHeaderValue();
            }
        }


        #region "Constructors"
        public CloudElementsConnector(string elementsURL)
        {
            if (elementsURL != null) ElementsPublicUrl = DefaultElementsPublicURL;
        }


        public CloudElementsConnector()
        {
            InstanceCreated = DateTime.Now;
            ElementsPublicUrl = DefaultElementsPublicURL;
            APIClient = NewHttpClient();
            
            lock (StaticLockObject) {
                ++ConnectorInstanceCounter;
                ConnectorInstanceNumber = ConnectorInstanceCounter;
            }
        }

        /// <summary>
        /// Returns another APIConnector with the same authorization and trace handler
        /// </summary>
        /// <returns></returns>
        public CloudElementsConnector Clone()
        {
            CloudElementsConnector result = new CloudElementsConnector();
            result.ElementsPublicUrl = this.ElementsPublicUrl;
            result.APIAuthorization = this.AuthorizationData;
            result.DiagTrace = this.DiagTrace;
            return result;
        }
        #endregion

        public async Task<Pong> Ping()
        {
            HttpResponseMessage response = await APIExecuteGet("hubs/documents/ping");
            Pong ResultPong = await response.Content.ReadAsAsync<Pong>();
            return ResultPong;
        }

        /// <summary>
        /// (optional) Releases resources and authorization used by this instance and sends summary diag event
        /// </summary>
        public void Close()
        {
            if (InstanceRequestCounter > 0)
            {
                string traceInfo = string.Format("ce(close,) {0}", GetStatisticsSummary());
                    //HttpClientInstanceCounter = 0;
                OnDiagTrace(traceInfo);
            }
            this.APIAuthorization = null;
            this.DiagTrace = null;
        }

        public string GetLastFailureInformation()
        {
            return LastFailureInformation;
        }

        public string GetStatisticsSummary()
        {
            string result;
            lock (StaticLockObject)
            {
                if (InstanceRequestCounter > 0)
                {
                    double LifeSpanMS = DateTime.Now.Subtract(InstanceCreated).TotalMilliseconds;
                    if (LifeSpanMS == 0) LifeSpanMS = 1;
                    string traceInfo = string.Format("#{0}/{1}  r={2}; Life={7:F1}s; Used={3:F1}s; Avg={4:F1}s; Busy={8:P1}; Connector Totals: r={5}, Used={6:F1}s ", ConnectorInstanceNumber, ConnectorInstanceCounter,
                                                        InstanceRequestCounter, InstanceTotalRequestMS / 1000d, InstanceTotalRequestMS / InstanceRequestCounter,
                                                        RequestCounter, TotalRequestMS / 1000d,
                                                        LifeSpanMS / 1000d, InstanceTotalRequestMS / LifeSpanMS);
                    //HttpClientInstanceCounter = 0;
                    result = traceInfo;
                }
                else result = "No work has been performed";
            }
            return result;
        }

        #region "documents/files and folders"

        /// <summary>
        /// Retrieves the amount of storage available on your cloud service account
        /// </summary>
        /// <returns></returns>
        public async Task<CloudStorage> GetStorageAvailable()
        {
            CloudStorage Result;
            HttpResponseMessage response = await APIExecuteGet("hubs/documents/storage");
            Result = await response.Content.ReadAsAsync<CloudStorage>();
            return Result;
        }


        /// <summary>
        /// Retrieves specific metadata on a file or folder associated with an ID from your cloud service using its specified path. 
        /// </summary>
        /// <param name="entryType">Specifies if the identifier is a file or a folder</param>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <returns></returns>
        public async Task<CloudFile> GetDocEntryMetaData(DirectoryEntryType entryType, FileSpecificationType fileSpecType, string identifier)
        {
            CloudFile Result;
            HttpResponseMessage response;
            string RequestURL;
            string URLEntryType = "files";
            if (entryType == DirectoryEntryType.Folder) URLEntryType = "folders";
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = "hubs/documents/{1}/{0}/metadata";
                    break;
                case FileSpecificationType.Path:
                    RequestURL = "hubs/documents/{1}/metadata?path={0}";
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }
            RequestURL = string.Format(RequestURL, System.Net.WebUtility.UrlEncode(identifier), URLEntryType);
            response = await APIExecuteGet(RequestURL);
            Result = await response.Content.ReadAsAsync<CloudFile>();
            return Result;
        }

        /// <summary>
        /// Update a file or folder metadata associated with an ID or path
        /// </summary>
        /// <param name="entryType">Specifies if the identifier is a file or a folder</param>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <param name="fileData"></param>
        /// <returns>update file data</returns>
        /// <remarks>Update a file's metadata (tags and path) associated with an ID. 
        /// For example, if you had a document that was tagged as operations but needed to be tagged as legal, then you would perform a PATCH to update the tag using the tags JSON array field. 
        /// The PATCH method can update the name or directory of a file as well as move a file by using the path JSON field. 
        /// You cannot update the size of a file. Specifying a file associated with an ID that does not exist results in an error response.</remarks>
        public async Task<CloudFile> PatchDocEntryMetaData(DirectoryEntryType entryType, FileSpecificationType fileSpecType, string identifier, CloudFile fileData)
        {

            HttpResponseMessage response;
            string RequestURL;
            string URLEntryType = "files";
            if (entryType == DirectoryEntryType.Folder) URLEntryType = "folders";
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = "hubs/documents/{1}/{0}/metadata";
                    break;
                case FileSpecificationType.Path:
                    RequestURL = "hubs/documents/{1}/metadata?path={0}";
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }

            RequestURL = string.Format(RequestURL, System.Net.WebUtility.UrlEncode(identifier), URLEntryType);
            var content = CloudFileRequestContent(fileData);
            response = await APIExecutePatch(RequestURL, content);
            fileData = await response.Content.ReadAsAsync<CloudFile>();
            return fileData;
        }
        #endregion

        #region "documents/folders/..."
        public async Task<List<CloudFile>> ListFolderContents(FileSpecificationType fileSpecType, string path, bool withTags)
        {
            HttpResponseMessage response;
            string RequestURL;
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = "hubs/documents/folders/{0}/contents?&fetchTags={1}";
                    break;
                case FileSpecificationType.Path:
                    RequestURL = "hubs/documents/folders/contents?path={0}&fetchTags={1}";
                    break;
                default:
                    throw new ArgumentException("Unsupported Folder Specification Type - " + fileSpecType.ToString());
            }

            response = await APIExecuteGet(string.Format(RequestURL, System.Net.WebUtility.UrlEncode(path), withTags));
            List<CloudFile> ResultList = await response.Content.ReadAsAsync<List<CloudFile>>();
            return ResultList;
        }

        public async Task<CloudFile> Copy(CloudFile sourceFile, string identifier, string targetPath)
        {
            return await Copy(sourceFile.EntryType, FileSpecificationType.ID, sourceFile.id, targetPath);
        }

        /// <summary>
        /// Copies a file or folder to the specified target
        /// </summary>
        /// <param name="entryType"></param>
        /// <param name="fileSpecType"></param>
        /// <param name="identifier"></param>
        /// <param name="targetPath"></param>
        /// <returns></returns>
        public async Task<CloudFile> Copy(DirectoryEntryType entryType, FileSpecificationType fileSpecType, string identifier, string targetPath)
        {

            HttpResponseMessage response;
            string RequestURL;
            string URLEntryType = "files";
            if (entryType == DirectoryEntryType.Folder) URLEntryType = "folders";
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = "hubs/documents/{1}/{0}/copy";
                    break;
                case FileSpecificationType.Path:
                    RequestURL = "hubs/documents/{1}/copy?path={0}";
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }

            RequestURL = string.Format(RequestURL, System.Net.WebUtility.UrlEncode(identifier), URLEntryType);
            CloudFile fileData = new CloudFile();
            fileData.path = targetPath;
            var content = CloudFileRequestContent(fileData);
            response = await APIExecutePost(RequestURL, content);
            fileData = await response.Content.ReadAsAsync<CloudFile>();
            return fileData;
        }


        /// <summary>
        /// Creates a folder
        /// </summary>
        /// <param name="path">full path name of folder, must begin with a slash</param>
        /// <param name="tags">optional tags</param>
        /// <returns>Metadata for new folder</returns>
        /// <remarks></remarks>
        public async Task<CloudFile> CreateFolder(string path, string[] tags)
        {
            CloudFile newFolder = new CloudFile();
            newFolder.path = path;
            newFolder.tags = tags;
            return await CreateFolder(newFolder);
        }

        /// <summary>
        /// Creates a folder
        /// </summary>
        /// <param name="newFolder">Must have value in path that includes the full path name of folder, must begin with a slash</param>
        /// <returns></returns>
        public async Task<CloudFile> CreateFolder(CloudFile newFolder)
        {
            string URL = "hubs/documents/folders";

            if (!newFolder.path.StartsWith("/")) throw new ArgumentException("Path must begin with a slash");

            var content = CloudFileRequestContent(newFolder);
            HttpResponseMessage response = await APIExecutePost(URL, content);
            CloudFile Result = await response.Content.ReadAsAsync<CloudFile>();
            return Result;

        }

        public async Task<CloudFile> DeleteFolder(string path, Boolean withTrash)
        {
            HttpResponseMessage response = await APIExecuteDelete(string.Format("hubs/documents/folders?path={0}&emptyTrash={1}", System.Net.WebUtility.UrlEncode(path), withTrash));
            CloudFile ResultList = await response.Content.ReadAsAsync<CloudFile>();
            return ResultList;
        }


        /// <summary>
        /// Retrieves specific metadata on a file associated with an ID from your cloud service using its specified path. 
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <returns></returns>
        public async Task<CloudFile> GetFolderMetaData(FileSpecificationType fileSpecType, string identifier)
        {
            CloudFile Result;
            Result = await GetDocEntryMetaData(DirectoryEntryType.Folder, fileSpecType, identifier);
            return Result;
        }

        /// <summary>
        /// Update a file metadata associated with an ID or path
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <param name="fileData"></param>
        /// <returns>update file data</returns>
        /// <remarks>Update a file's metadata (tags and path) associated with an ID. 
        /// For example, if you had a document that was tagged as operations but needed to be tagged as legal, then you would perform a PATCH to update the tag using the tags JSON array field. 
        /// The PATCH method can update the name or directory of a file as well as move a file by using the path JSON field. 
        /// You cannot update the size of a file. Specifying a file associated with an ID that does not exist results in an error response.</remarks>
        public async Task<CloudFile> PatchFolderMetaData(FileSpecificationType fileSpecType, string identifier, CloudFile fileData)
        {
            fileData = await PatchDocEntryMetaData(DirectoryEntryType.Folder, fileSpecType, identifier, fileData);
            return fileData;
        }
        #endregion

        #region "documents/files/..."

        /// <summary>
        /// Deletes a specific file from your cloud service   
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <param name="emptyTrash">true also empties trash</param>
        /// <returns></returns>
        /// <remarks>Specifying a file associated with an ID that does not exist results in an error response.</remarks>
        public async Task<bool> DeleteFile(FileSpecificationType fileSpecType, string identifier, bool emptyTrash)
        {
            bool Result;
            HttpResponseMessage response;
            string RequestURL;

            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = "hubs/documents/files/{0}?emptyTrash={1}";
                    break;
                case FileSpecificationType.Path:
                    RequestURL = "hubs/documents/files?path={0}&emptyTrash={1}";
                    break;
                default:
                    throw new ArgumentException("Unsupported Folder Specification Type - " + fileSpecType.ToString());
            }

            response = await APIExecuteDelete(string.Format(RequestURL, System.Net.WebUtility.UrlEncode(identifier), emptyTrash));
            Result = true;
            return Result;
        }




        /// <summary>
        /// Returns a link that can be used to download the specified file through Cloud Elements. The link can be used to download the file without providing credentials. 
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <returns></returns>
        /// <remarks>Specifying a file that does not exist results in an error.</remarks>
        public async Task<CloudLink> FileLinks(FileSpecificationType fileSpecType, string identifier)
        {
            CloudLink Result;
            HttpResponseMessage response;
            string RequestURL;
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = string.Format("hubs/documents/files/{0}/links", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                case FileSpecificationType.Path:
                    RequestURL = string.Format("hubs/documents/files/links?path={0}", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }
            response = await APIExecuteGet(RequestURL);
            Result = await response.Content.ReadAsAsync<CloudLink>();
            return Result;
        }


        /// <summary>
        /// Retrieves specific metadata on a file associated with an ID from your cloud service using its specified path. 
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <returns></returns>
        public async Task<CloudFile> GetFileMetaData(FileSpecificationType fileSpecType, string identifier)
        {
            CloudFile Result;
            HttpResponseMessage response;
            string RequestURL;
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = string.Format("hubs/documents/files/{0}/metadata", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                case FileSpecificationType.Path:
                    RequestURL = string.Format("hubs/documents/files/metadata?path={0}", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }
            response = await APIExecuteGet(RequestURL);
            Result = await response.Content.ReadAsAsync<CloudFile>();
            return Result;
        }

        /// <summary>
        /// Update a file metadata associated with an ID or path
        /// </summary>
        /// <param name="fileSpecType">Specifies if the identifier is an ID or a PATH</param>
        /// <param name="identifier">Specifying an ID that does not exist results in an error response.</param>
        /// <param name="fileData"></param>
        /// <returns>update file data</returns>
        /// <remarks>Update a file's metadata (tags and path) associated with an ID. 
        /// For example, if you had a document that was tagged as operations but needed to be tagged as legal, then you would perform a PATCH to update the tag using the tags JSON array field. 
        /// The PATCH method can update the name or directory of a file as well as move a file by using the path JSON field. 
        /// You cannot update the size of a file. Specifying a file associated with an ID that does not exist results in an error response.</remarks>
        public async Task<CloudFile> PatchFileMetaData(FileSpecificationType fileSpecType, string identifier, CloudFile fileData)
        {

            HttpResponseMessage response;
            string RequestURL;
            switch (fileSpecType)
            {
                case FileSpecificationType.ID:
                    RequestURL = string.Format("hubs/documents/files/{0}/metadata", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                case FileSpecificationType.Path:
                    RequestURL = string.Format("hubs/documents/files/metadata?path={0}", System.Net.WebUtility.UrlEncode(identifier));
                    break;
                default:
                    throw new ArgumentException("unsupported File Specification Type - " + fileSpecType.ToString());
            }

            var content = CloudFileRequestContent(fileData);
            response = await APIExecutePatch(RequestURL, content);
            fileData = await response.Content.ReadAsAsync<CloudFile>();
            return fileData;
        }

        public async Task<FileContent> GetFile(string id)
        {
            HttpResponseMessage response = await APIExecuteGet(string.Format("hubs/documents/files/{0}", System.Net.WebUtility.UrlEncode(id)));
            FileContent Result = new FileContent(response);
            Result.ContentStream = await response.Content.ReadAsStreamAsync();
            return Result;
        }


        /// <summary>
        /// Uploads a file from a stream
        /// </summary>
        /// <param name="uploadSource"></param>
        /// <param name="contentType">image/jpeg or whatever is appropriate for the file type</param>
        /// <param name="path">full path and file name for cloud storage</param>
        /// <param name="description"></param>
        /// <param name="tags">Array of strings</param>
        /// <param name="overwrite"></param>
        /// <param name="sizeInBytes">Only required for Sharepoint; ignored if less than or = 0</param>
        /// <returns>metadata for the newly uploaded file</returns>
        public async Task<CloudFile> PostFile(System.IO.Stream uploadSource, string contentType, string path,
                                                string description,
                                                string[] tags,
                                                Boolean overwrite,
                                                long sizeInBytes)
        {
            string URL = string.Format("hubs/documents/files?path={0}&description={1}"
                                        , System.Net.WebUtility.UrlEncode(path)
                                        , System.Net.WebUtility.UrlEncode(description)
                                        );
            // neither is RFC3986 compliant
            // System.Net.WebUtility.UrlEncode(path); or System.Uri.EscapeDataString(path); ??? see http://blog.nerdbank.net/2009/05/uriescapedatapath-and.html
            if (overwrite) URL += "&overwrite=true";
            if (sizeInBytes > 0) URL += "&size=" + sizeInBytes.ToString();
            if ((tags != null) && (tags.Length > 0))
            {
                var tagCnt = 0;
                foreach (string tag in tags)
                {
                    if ((tag != null) && (tag.Trim().Length > 0))
                    {
                        if (tagCnt == 0) URL += "&tags%5B%5D=";
                        else URL += ",";
                        tagCnt++;
                        URL += System.Net.WebUtility.UrlEncode(tag.Trim());
                    }
                }
            }

            // ref http://stackoverflow.com/questions/16416601/c-sharp-httpclient-4-5-multipart-form-data-upload

            var content = new MultipartFormDataContent(String.Format("----------{0:N}", Guid.NewGuid()));
            var FileContentSection = new StreamContent(uploadSource, 16384);
            content.Add(FileContentSection);
            FileContentSection.Headers.ContentDisposition.Name = "file";
            FileContentSection.Headers.ContentDisposition.FileName = System.IO.Path.GetFileName(path);
            FileContentSection.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);


            HttpResponseMessage response = await APIExecutePost(URL, content);
            CloudFile Result = await response.Content.ReadAsAsync<CloudFile>();
            return Result;

        }




        #endregion

        // ----------------------------

        #region "Request Support"

        void OnDiagTrace(string info)
        {
            if (DiagTrace != null)
            {
                DiagTrace(this, info);
            }
            else
            {
                info = string.Format("{0}: {1}", Tools.TraceTimeNow(), info);
                System.Diagnostics.Trace.WriteLineIf(WriteDiagTrace, info);
            }
        }

        HttpContent CloudFileRequestContent(CloudFile filedata)
        {
            Newtonsoft.Json.JsonSerializerSettings jsonSettings = new Newtonsoft.Json.JsonSerializerSettings();
            jsonSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(filedata, jsonSettings);

            var content = new StringContent(jsonData);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return content;
        }

        async Task<HttpResponseMessage> APIExecuteGet(string URI)
        {
            return await APIExecuteVerb(HttpVerb.Get, URI);
        }

        async Task<HttpResponseMessage> APIExecuteDelete(string URI)
        {
            return await APIExecuteVerb(HttpVerb.Delete, URI, null);
        }

        async Task<HttpResponseMessage> APIExecutePost(string URI, HttpContent content)
        {
            return await APIExecuteVerb(HttpVerb.Post, URI, content);
        }

        async Task<HttpResponseMessage> APIExecutePatch(string URI, HttpContent content)
        {
            return await APIExecuteVerb(HttpVerb.Patch, URI, content);
        }

        enum HttpVerb
        {
            Get,
            Delete,
            Post,
            Patch
        }

        public enum FileSpecificationType
        {
            ID,
            Path
        }

        public enum DirectoryEntryType
        {
            Folder,
            File
        }


        public enum TraceLevel
        {
            NonSuccess,
            All,
            Verbose
        }

        async Task<HttpResponseMessage> APIExecuteVerb(HttpVerb verb, string URI)
        {
            return await APIExecuteVerb(verb, URI, null);
        }

        async Task<HttpResponseMessage> APIExecuteVerb(HttpVerb verb, string URI, HttpContent content)
        {
            DateTime startms = DateTime.Now;
            lock (StaticLockObject) RequestCounter++;
            InstanceRequestCounter++;
            HttpResponseMessage response;
            Task<HttpResponseMessage> HttpRequestTask;
            lock (APIClient) // not clear that this is required, but...
            {
                switch (verb)
                {
                    case HttpVerb.Get:
                        HttpRequestTask = APIClient.GetAsync(URI, HttpCompletionOption.ResponseHeadersRead);
                        break;
                    case HttpVerb.Delete:
                        HttpRequestTask = APIClient.DeleteAsync(URI);
                        break;
                    case HttpVerb.Post:
                        if (content == null) throw new ArgumentException("Post requests require content");
                        HttpRequestTask = APIClient.PostAsync(URI, content);
                        break;
                    case HttpVerb.Patch:
                        if (content == null) throw new ArgumentException("Patch requests require content");
                        var request = new HttpRequestMessage(new HttpMethod("PATCH"), URI) { Content = content };
                        HttpRequestTask = APIClient.SendAsync(request);
                        break;
                    default:
                        throw new ApplicationException("Unsupported verb");
                }
            }
            response = await HttpRequestTask;
            double msUsed = DateTime.Now.Subtract(startms).TotalMilliseconds;
            lock (StaticLockObject) TotalRequestMS += msUsed;
            InstanceTotalRequestMS += msUsed;
            LastFailureInformation = "";
            if ((!response.IsSuccessStatusCode) || (DiagOutputLevel > TraceLevel.NonSuccess))
            {
                string traceInfo = string.Format("ce({0},{1}) s={3:F1}; status={2}", verb,URIForLogging( URI), response.StatusCode, msUsed / 1000.0);
                if ((!response.IsSuccessStatusCode) && (response.Content.Headers.ContentLength > 0))
                {
                    Newtonsoft.Json.Linq.JObject info = await response.Content.ReadAsAsync<Newtonsoft.Json.Linq.JObject>();
                    if (info != null)
                    {
                        Newtonsoft.Json.Linq.JToken msgtoken = info.GetValue("message");
                        Newtonsoft.Json.Linq.JToken rqIDtoken = info.GetValue("requestId");
                        if (rqIDtoken != null)  traceInfo = string.Concat(traceInfo, "; RequestId=", rqIDtoken.ToString());
                        if (msgtoken != null)
                        {
                            traceInfo = string.Concat(traceInfo, " - ", msgtoken.ToString());
                            LastFailureInformation = msgtoken.ToString();
                            if (rqIDtoken != null) LastFailureInformation = string.Format("{0}; (Request #{1}, ID {2})" , LastFailureInformation, RequestCounter, rqIDtoken.ToString());
                        }
                    }
                }
                OnDiagTrace(traceInfo);
            }
            response.EnsureSuccessStatusCode();
            return response;
        }

        string URIForLogging(string rawURI)
        {
            if (SimplifyLoggedURIs) rawURI = rawURI.Replace("%2F", "/");
                return rawURI;
        }

        HttpClient NewHttpClient()
        {
            HttpClient client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.GZip });
            lock (StaticLockObject) ++HttpClientInstanceCounter;
            client.BaseAddress = new Uri(ElementsPublicUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(StringWithQualityHeaderValue.Parse("gzip"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(StringWithQualityHeaderValue.Parse("deflate"));

            return (client);
        }
        #endregion





    }

    public class Pong
    {
        public string dateTime;
        public string endpoint;
        public override string ToString()
        {
            return string.Format("Pong[{0},{1}]", dateTime, endpoint);
        }
    }

    public class CloudLink
    {
        public string expires;  //  optional
        public string cloudElementsLink; //  optional
        public string providerViewLink; //  optional
        public string providerLink; //  optional
    }

    public class CloudStorage
    {
        public Int64 shared; //  optional),
        public Int64 total; //  optional),
        public Int64 used; //  optional),
    }

    public class FileContent
    {
        public readonly long ContentLength;
        public readonly string Disposition;
        public System.IO.Stream ContentStream;
        public FileContent(HttpResponseMessage response)
        {
            ContentLength = 0;
            if (response.Content.Headers.ContentLength != null) ContentLength = (long)response.Content.Headers.ContentLength;
            Disposition = "";
            if (response.Content.Headers.ContentDisposition != null) Disposition = (string)response.Content.Headers.ContentDisposition.FileName;
        }
    }

}
