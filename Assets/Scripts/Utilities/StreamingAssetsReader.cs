using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace TirumalaAR.Utilities
{
    /// <summary>
    /// Reads text out of StreamingAssets.
    ///
    /// On Android StreamingAssets lives inside the compressed APK, so File.ReadAllText cannot see
    /// it — the path is a "jar:file://" URL that only UnityWebRequest can open. On every other
    /// platform the direct file read is faster and avoids a frame of latency. Both paths are
    /// handled here so callers never have to care.
    /// </summary>
    public static class StreamingAssetsReader
    {
        public sealed class ReadResult
        {
            public string text;
            public string error;
            public bool Success => error == null && text != null;
        }

        public static IEnumerator ReadText(string relativePath, ReadResult result)
        {
            if (result == null)
                yield break;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                result.error = "No path supplied.";
                yield break;
            }

            var fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

            // A URI scheme in the path means it must go through UnityWebRequest.
            if (fullPath.Contains("://") || fullPath.Contains(":///"))
            {
                using var request = UnityWebRequest.Get(fullPath);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    result.error = $"Could not read '{relativePath}': {request.error}";
                    yield break;
                }

                result.text = request.downloadHandler.text;
                yield break;
            }

            string text = null;
            string error = null;

            try
            {
                if (File.Exists(fullPath))
                    text = File.ReadAllText(fullPath);
                else
                    error = $"File not found: {fullPath}";
            }
            catch (Exception e)
            {
                error = $"Could not read '{relativePath}': {e.Message}";
            }

            result.text = text;
            result.error = error;
        }
    }
}
