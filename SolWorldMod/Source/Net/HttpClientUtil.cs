// SolWorldMod/Source/Net/HttpClientUtil.cs
using System;
using System.IO;
using System.Net;
using System.Text;
using Verse;

namespace SolWorldMod.Net
{
    public static class HttpClientUtil
    {
        private static readonly int TimeoutMs = 30000; // 30 seconds

        public static string Get(string url)
        {
            try
            {
                Log.Message("SolWorld: HTTP GET " + url);
                
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = TimeoutMs;
                request.UserAgent = "SolWorld-RimWorld-Mod/1.0";
                request.Accept = "application/json";

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream))
                        {
                            var result = reader.ReadToEnd();
                            Log.Message("SolWorld: HTTP GET success - " + result.Length + " chars");
                            return result;
                        }
                    }
                    else
                    {
                        Log.Error("SolWorld: HTTP GET failed - Status: " + response.StatusCode);
                        return null;
                    }
                }
            }
            catch (WebException ex)
            {
                Log.Error("SolWorld: HTTP GET WebException: " + ex.Message);
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    Log.Error("SolWorld: HTTP Error Status: " + errorResponse.StatusCode);
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: HTTP GET Exception: " + ex.Message);
                return null;
            }
        }

        public static string Post(string url, string jsonData)
        {
            try
            {
                Log.Message("SolWorld: HTTP POST " + url);
                Log.Message("SolWorld: POST Data: " + jsonData.Substring(0, Math.Min(200, jsonData.Length)) + "...");
                
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = TimeoutMs;
                request.ContentType = "application/json";
                request.UserAgent = "SolWorld-RimWorld-Mod/1.0";
                request.Accept = "application/json";

                // Write JSON data
                var data = Encoding.UTF8.GetBytes(jsonData);
                request.ContentLength = data.Length;

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream))
                        {
                            var result = reader.ReadToEnd();
                            Log.Message("SolWorld: HTTP POST success - " + result.Length + " chars");
                            return result;
                        }
                    }
                    else
                    {
                        Log.Error("SolWorld: HTTP POST failed - Status: " + response.StatusCode);
                        return null;
                    }
                }
            }
            catch (WebException ex)
            {
                Log.Error("SolWorld: HTTP POST WebException: " + ex.Message);
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    using (var stream = errorResponse.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        var errorBody = reader.ReadToEnd();
                        Log.Error("SolWorld: HTTP Error Body: " + errorBody);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("SolWorld: HTTP POST Exception: " + ex.Message);
                return null;
            }
        }
    }
}