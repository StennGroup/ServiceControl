namespace ServiceControl.CompositeViews.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Nancy;
    using NServiceBus.Logging;
    using Raven.Client;
    using ServiceBus.Management.Infrastructure.Nancy.Modules;
    using ServiceBus.Management.Infrastructure.Settings;
    using ServiceControl.Infrastructure.Settings;
    using HttpStatusCode = System.Net.HttpStatusCode;

    public abstract class RoutedApi<TIn> : IApi
    {
        static ILog logger = LogManager.GetLogger(typeof(RoutedApi<TIn>));

        private static HashSet<string> contentHeaders = new HashSet<string>
        {
            "Allow",
            "Content-Disposition",
            "Content-Encoding",
            "Content-Language",
            "Content-Length",
            "Content-Location",
            "Content-MD5",
            "Content-Range",
            "Content-Type",
            "Expires",
            "Last-Modified"
        };

        public IDocumentStore Store { get; set; }
        public Settings Settings { get; set; }
        public Func<HttpClient> HttpClientFactory { get; set; }

        public async Task<Response> Execute(BaseModule module, TIn input)
        {
            var currentRequest = module.Request;
            Response response;

            var instanceId = GetInstance(currentRequest, input);

            var localInstanceId = InstanceIdGenerator.FromApiUrl(Settings.ApiUrl);

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                if (instanceId == localInstanceId)
                {
                    response = await LocalQuery(currentRequest, input, localInstanceId);
                }
                else
                {
                    response = await RemoteCall(currentRequest, instanceId);
                }
            }
            else
            {
                response = await LocalQuery(currentRequest, input, localInstanceId);
            }

            return response;
        }

        protected virtual string GetInstance(Request currentRequest, TIn input)
        {
            return (string)currentRequest.Query.instance_id;
        }

        protected abstract Task<Response> LocalQuery(Request request, TIn input, string instanceId);

        private async Task<Response> RemoteCall(Request currentRequest, string instanceId)
        {
            var remoteUri = InstanceIdGenerator.ToApiUrl(instanceId);

            var instanceUri = new Uri($"{remoteUri}{currentRequest.Path}{currentRequest.Url.Query}");
            var httpClient = HttpClientFactory();
            try
            {
                var method = new HttpMethod(currentRequest.Method);
                var requestMessage = new HttpRequestMessage(method, instanceUri);
                var streamContent = new StreamContent(currentRequest.Body);
                requestMessage.Content = streamContent;
                foreach (var currentRequestHeader in currentRequest.Headers)
                {
                    if (contentHeaders.Contains(currentRequestHeader.Key))
                    {
                        streamContent.Headers.Add(currentRequestHeader.Key, currentRequestHeader.Value);
                    }
                    else
                    {
                        requestMessage.Headers.Add(currentRequestHeader.Key, currentRequestHeader.Value);
                    }
                }

                var rawResponse = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);

                // TODO: is it correct to merge headers??
                var headers = rawResponse.Headers.Union(rawResponse.Content.Headers).ToDictionary(k => k.Key, v => v.Value.FirstOrDefault());
                var httpStatusCode = (Nancy.HttpStatusCode) Enum.Parse(typeof(HttpStatusCode), rawResponse.StatusCode.ToString(), ignoreCase: true);

                return new Response
                {
                    Contents = stream =>
                    {
                        if (httpStatusCode == Nancy.HttpStatusCode.NotFound)
                        {
                            Response.NoBody(stream);
                        }
                        else
                        {
                            rawResponse.Content.CopyToAsync(stream).GetAwaiter().GetResult();
                        }
                    },
                    Headers = headers,
                    ContentType = rawResponse.Content.Headers.ContentType.ToString(),
                    StatusCode = httpStatusCode
                };
            }
            catch (Exception exception)
            {
                logger.Warn($"Failed to query remote instance at {remoteUri}.", exception);

                return new Response
                {
                    StatusCode = Nancy.HttpStatusCode.InternalServerError,
                };
            }

        }
    }
}