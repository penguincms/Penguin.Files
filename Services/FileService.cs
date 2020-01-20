﻿using Penguin.Cms.Modules.Files.Constants.Strings;
using Penguin.Configuration.Abstractions.Interfaces;
using Penguin.Debugging;
using Penguin.DependencyInjection.Abstractions.Interfaces;
using Penguin.Files.Abstractions;
using Penguin.Security.Abstractions.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;

namespace Penguin.Files.Services
{
    /// <summary>
    /// A collection of methods used to simplify informational access to the local file system
    /// </summary>
    public class FileService : IRegisterMostDerived
    {
        /// <summary>
        /// Represents a list of files that have been checked for existence, to prevent superflous hard drive reads. Key is path, Value is last determination of existence
        /// </summary>
        public static ConcurrentDictionary<string, bool> KnownFiles { get; } = new ConcurrentDictionary<string, bool>();

        /// <summary>
        /// Returns the current executing directory (not overridden)
        /// </summary>
        public virtual string ApplicationPath => Directory.GetCurrentDirectory();

        protected IProvideConfigurations ConfigurationProvider { get; set; }

        /// <summary>
        /// If the executing directory was overridden, this will not be null
        /// </summary>
        protected string ExecutionPathOverride { get; set; }

        protected IUserSession UserSession { get; set; }
        private static object WatcherLock { get; set; } = new object();

        private bool IsCaseSensitive
        {
            get
            {
                if (!isCaseSensitive.HasValue)
                {
                    string currentDir = Directory.GetCurrentDirectory();

                    if (Directory.Exists(currentDir.ToLower(CultureInfo.CurrentCulture)) && Directory.Exists(currentDir.ToUpper(CultureInfo.CurrentCulture)))
                    {
                        isCaseSensitive = false;
                    }
                    else
                    {
                        isCaseSensitive = true;
                    }
                }

                return isCaseSensitive.Value;
            }
        }

        private static FileSystemWatcher Watcher;

        private bool? isCaseSensitive;

        /// <summary>
        /// Instantiates this class, and creates a file system watcher (if null) to send messages back to the service
        /// if any changes occur during execution
        /// </summary>
        public FileService(IUserSession userSession = null, IProvideConfigurations configurationProvider = null)
        {
            UserSession = userSession;
            ConfigurationProvider = configurationProvider;

            lock (WatcherLock)
            {
                if (Watcher is null)
                {
                    Watcher = new FileSystemWatcher
                    {
                        IncludeSubdirectories = true,
                        Path = ApplicationPath,
                    };

                    Watcher.Renamed += Watcher_Event;
                    Watcher.Deleted += Watcher_Event;
                    Watcher.Created += Watcher_Event;
                    Watcher.Changed += Watcher_Event;
                    Watcher.EnableRaisingEvents = true;
                }
            }
        }

        public static void FillData(IFile df)
        {
            if (df is null)
            {
                throw new ArgumentNullException(nameof(df));
            }

            if (df.Data.Length == 0 && File.Exists(df.FullName))
            {
                df.Data = File.ReadAllBytes(df.FullName);

                /// <summary>
                /// Checks if a file exists on disk. This is here because IO on the disk is VERY slow
                /// </summary>
                /// <param name="Uri"></param>
                /// <returns></returns>
            }
        }

        public bool Exists(string Uri)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(Uri));

            string toMatch = TrimTilde(Uri).Replace("/", "\\");

            

            if (!IsCaseSensitive)
            {
                toMatch = toMatch.ToLower(CultureInfo.CurrentCulture);
            }

            if (KnownFiles.TryGetValue(Uri, out bool result))
            {
                return result;
            }
            else
            {
                result = File.Exists(Path.Combine(ApplicationPath, toMatch));

                KnownFiles.TryAdd(toMatch, result);

                return result;
            }
        }

        public string GetUserFilesRoot()
        {
            string root = ConfigurationProvider.GetConfiguration(ConfigurationNames.USER_FILES_ROOT);

            if (string.IsNullOrWhiteSpace(root))
            {
                root = "Files";
            }

            root = root.Replace("\\", "/");

            if (root.StartsWith("~"))
            {
                root = root.Substring(1);
            }
            if (root.StartsWith("/"))
            {
                root = root.Substring(1);
            }
            return Path.Combine(Directory.GetCurrentDirectory(), root);
        }

        public string GetUserHome(IUser u = null)
        {
            u = u ?? UserSession.LoggedInUser;

            if (u is null)
            {
                throw new Exception("Can not get user home for null user when no active user session exists");
            }

            return Path.Combine(GetUserFilesRoot(), u.Guid.ToString());
        }

        /// <summary>
        /// Overrides the internal determination of the executing directory
        /// </summary>
        /// <param name="Root">The new directory to set as the execution root</param>
        public void SetExecutionPath(string Root) => ExecutionPathOverride = Root;

        public void StoreOnDisk(IFile df)
        {
            try
            {
                if (df != null && df.Data != null && df.Data.Length > 0)
                {
                    byte[] toStore = df.Data;
                    df.Data = Array.Empty<byte>();

                    FileInfo fileInfo = new FileInfo(df.FullName);

                    if (string.IsNullOrWhiteSpace(df.FullName))
                    {
                        string RawPath = GetUserFilesRoot();

                        df.FullName = Path.Combine(RawPath, Guid.NewGuid().ToString().Replace("-", ""));
                    }

                    if (!fileInfo.Directory.Exists)
                    {
                        fileInfo.Directory.Create();
                    }

                    File.WriteAllBytes(df.FullName, toStore);
                }
            }
            catch (Exception ex)
            {
                StaticLogger.Log($"Failed to persist file data to disk {df?.FullName}: ", StaticLogger.LoggingLevel.Call);
                StaticLogger.Log(ex.Message, StaticLogger.LoggingLevel.Call);
            }
        }

        /// <summary>
        /// If the path starts with ~/ this method strips that off
        /// </summary>
        /// <param name="instr">The string to strip</param>
        /// <returns>The path without ~/</returns>
        protected static string TrimTilde(string instr)
        {
            Contract.Requires(instr != null);

            string toMatch = instr;

            if (toMatch.StartsWith("~/", System.StringComparison.OrdinalIgnoreCase))
            {
                toMatch = toMatch.Substring(2);
            }

            return toMatch;
        }

        private static void Watcher_Event(object sender, FileSystemEventArgs e) => KnownFiles.Clear();
    }
}