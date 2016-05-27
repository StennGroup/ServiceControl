﻿namespace ServiceControl.Operations.BodyStorage.RavenAttachments
{
    using System.IO;
    using Raven.Client;
    using Raven.Json.Linq;

    public class RavenAttachmentsBodyStorage : IBodyStorage
    {
        public IDocumentStore DocumentStore { get; set; }

        public string Store(string bodyId, string contentType, int bodySize, Stream bodyStream)
        {
            DocumentStore.DatabaseCommands.PutAttachment("messagebodies/" + bodyId, null, bodyStream, new RavenJObject
            {
                {"ContentType", contentType},
                {"ContentLength", bodySize}
            });

            return $"/messages/{bodyId}/body";
        }

        public bool TryFetch(string bodyId, out Stream stream)
        {
            var attachment = DocumentStore.DatabaseCommands.GetAttachment("messagebodies/" + bodyId);

            if (attachment == null)
            {
                stream = null;
                return false;
            }

            stream = attachment.Data();
            return true;
        }
    }
}