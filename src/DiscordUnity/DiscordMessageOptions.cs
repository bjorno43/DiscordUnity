using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace DiscordUnity
{
    public sealed class DiscordFile
    {
        public string Filename { get; set; }
        public byte[] Content { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
    }

    public sealed class DiscordMessageOptions
    {
        public string Content { get; set; }
        public string Nonce { get; set; }
        public bool? Tts { get; set; }
        public object[] Embeds { get; set; }
        public object AllowedMentions { get; set; }
        public object MessageReference { get; set; }
        public int? Flags { get; set; }
        [JsonIgnore]
        public DiscordFile[] Files { get; set; }

        internal static HttpContent Multipart(JObject payload, DiscordFile[] files)
        {
            var multipart = new MultipartFormDataContent();
            try
            {
                var json = (JObject)payload.DeepClone();
                json["attachments"] = JArray.FromObject(files.Select((file, index) => new { id = index, filename = file.Filename }));
                multipart.Add(new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json"), "payload_json");
                for (int i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    if (file.Content == null || string.IsNullOrWhiteSpace(file.Filename) || file.Filename.IndexOfAny(new[] { '\r', '\n', '"' }) >= 0)
                        throw new ArgumentException("Each file needs bytes and a valid filename.");
                    var content = new ByteArrayContent(file.Content);
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
                    multipart.Add(content, "files[" + i + "]", file.Filename);
                }
                return multipart;
            }
            catch { multipart.Dispose(); throw; }
        }
    }
}
