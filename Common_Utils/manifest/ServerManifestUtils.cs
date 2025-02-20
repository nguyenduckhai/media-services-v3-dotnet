﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Common_Utils
{
    public static class ServerManifestUtils
    {
        public static async Task<GeneratedServerManifest> LoadAndUpdateManifestTemplateAsync(BlobContainerClient container)
        {
            // Let's list the blobs
            var allBlobs = new List<BlobItem>();
            await foreach (Azure.Page<BlobItem> page in container.GetBlobsAsync().AsPages())
            {
                allBlobs.AddRange(page.Values);
            }
            IEnumerable<BlobItem> blobs = allBlobs.Where(c => c.Properties.BlobType == BlobType.Block).Select(c => c);


            BlobItem[] mp4AssetFiles = blobs.Where(f => f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)).ToArray();
            BlobItem[] m4aAssetFiles = blobs.Where(f => f.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToArray();
            BlobItem[] mediaAssetFiles = blobs.Where(f => f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (mediaAssetFiles.Length != 0)
            {
                // Prepare the manifest
                string mp4fileuniqueaudio = null;
                XDocument doc = XDocument.Load(Path.Combine(Environment.CurrentDirectory, @"./manifest/manifest.ism"));

                XNamespace ns = "http://www.w3.org/2001/SMIL20/Language";

                XElement bodyxml = doc.Element(ns + "smil");
                XElement body2 = bodyxml.Element(ns + "body");

                XElement switchxml = body2.Element(ns + "switch");

                // audio tracks (m4a)
                foreach (BlobItem file in m4aAssetFiles)
                {
                    switchxml.Add(new XElement(ns + "audio", new XAttribute("src", file.Name), new XAttribute("title", Path.GetFileNameWithoutExtension(file.Name))));
                }

                if (m4aAssetFiles.Length == 0)
                {
                    // audio track(s)
                    IEnumerable<BlobItem> mp4AudioAssetFilesName = mp4AssetFiles.Where(f =>
                                                               (f.Name.ToLower().Contains("audio") && !f.Name.ToLower().Contains("video"))
                                                               ||
                                                               (f.Name.ToLower().Contains("aac") && !f.Name.ToLower().Contains("h264"))
                                                               );

                    IOrderedEnumerable<BlobItem> mp4AudioAssetFilesSize = mp4AssetFiles.OrderBy(f => f.Properties.ContentLength);

                    string mp4fileaudio = (mp4AudioAssetFilesName.Count() == 1) ? mp4AudioAssetFilesName.FirstOrDefault().Name : mp4AudioAssetFilesSize.FirstOrDefault().Name; // if there is one file with audio or AAC in the name then let's use it for the audio track
                    switchxml.Add(new XElement(ns + "audio", new XAttribute("src", mp4fileaudio), new XAttribute("title", "audioname")));

                    if (mp4AudioAssetFilesName.Count() == 1 && mediaAssetFiles.Length > 1) //looks like there is one audio file and one other video files
                    {
                        mp4fileuniqueaudio = mp4fileaudio;
                    }
                }

                // video tracks
                foreach (BlobItem file in mp4AssetFiles)
                {
                    if (file.Name != mp4fileuniqueaudio) // we don't put the unique audio file as a video track
                    {
                        switchxml.Add(new XElement(ns + "video", new XAttribute("src", file.Name)));
                    }
                }

                // manifest filename
                string name = CommonPrefix(mediaAssetFiles.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToArray());
                if (string.IsNullOrEmpty(name))
                {
                    name = "manifest";
                }
                else if (name.EndsWith("_") && name.Length > 1) // i string ends with "_", let's remove it
                {
                    name = name.Substring(0, name.Length - 1);
                }
                name += ".ism";

                return new GeneratedServerManifest() { Content = doc.Declaration.ToString() + Environment.NewLine + doc.ToString(), FileName = name };
            }
            else
            {
                return new GeneratedServerManifest() { Content = null, FileName = string.Empty }; // no mp4 in asset
            }
        }

        private static string CommonPrefix(string[] ss)
        {
            if (ss.Length == 0)
            {
                return string.Empty;
            }

            if (ss.Length == 1)
            {
                return ss[0];
            }

            int prefixLength = 0;

            foreach (char c in ss[0])
            {
                foreach (string s in ss)
                {
                    if (s.Length <= prefixLength || s[prefixLength] != c)
                    {
                        return ss[0].Substring(0, prefixLength);
                    }
                }
                prefixLength++;
            }

            return Slugify(ss[0]); // all strings identical
        }

        public static string ReturnS(int number)
        {
            return number > 1 ? "s" : string.Empty;
        }

        private static string Slugify(this string phrase)
        {
            string str = phrase.RemoveAccent().ToLower();
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", ""); // Remove all non valid chars          
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim(); // convert multiple spaces into one space  
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "-"); // //Replace spaces by dashes
            return str;
        }

        private static string RemoveAccent(this string txt)
        {
            byte[] bytes = System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(txt);
            return System.Text.Encoding.ASCII.GetString(bytes);
        }
    }
}
