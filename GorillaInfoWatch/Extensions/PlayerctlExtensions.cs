using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GorillaInfoWatch.Extensions
{
    public class PlayerctlExtensions
    {
        private readonly string _url = "http://127.0.0.1:6767"; // can change it but make sure to change it in the app too
        
        private async Task<string> Get(string endpoint)
        {
            using (UnityWebRequest www = UnityWebRequest.Get($"{_url}/{endpoint}"))
            {
                var operation = www.SendWebRequest();
                
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    return www.downloadHandler.text;
                }
                Debug.LogError($"failed because {www.error}");
                return string.Empty;
            }
        }
        
        public async Task<string> GetCover() => await Get("cover");
        public async Task<string> GetStatus() => await Get("status");
        public async Task<string> GetMetadata() => await Get("metadata");
        public async Task<string> GetArtist() => await Get("artist");
        public async Task<string> GetTitle() => await Get("title");
        public async Task<string> GetDuration() => await Get("duration");
        public async Task<string> GetPosition() => await Get("position");
        
        public async Task<string> SendCommand(string command)
        {
            return await Get($"cmd?op={command}");
        }
    }
}