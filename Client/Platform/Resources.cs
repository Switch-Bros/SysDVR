﻿using FFmpeg.AutoGen;
using SDL2;
using SysDVR.Client.Core;
using SysDVR.Client.GUI.Components;
using SysDVR.Client.Targets.Player;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SysDVR.Client.Platform
{
	internal static class Resources
	{
#if ANDROID_LIB
        // Android is such a shit platform that you can't fopen(), all resources must be read like this
        // Previously we used SDL file access API but on 32bit andorid they seem to fail with Invalid RWops
        public static byte[] ReadResource(string path)
        {
            Console.WriteLine($"Loading resource {path}");

            var res = Program.Native.ReadAssetFile(path, out var buffer, out var size);
            if (res != 0)
                throw new Exception($"Loading resource {path} failed with error code {res}");

            Console.WriteLine($"Resource loaded with size {size}");
            var data = new byte[size];
            unsafe {
                var unmanagedMem = new Span<byte>((byte*)buffer, size);
                unmanagedMem.CopyTo(data);
            }

            Console.WriteLine($"Freeing the unmanaged resource buffer {buffer:x}");
            Program.Native.FreeDynamicBuffer(buffer);
            return data;
        }

        static unsafe string[] GetTranslationFiles()
        {
            var res = new List<string>();
            if (Program.Native.IterateAssetsContent is not null)
            {
                Program.Native.IterateAssetsContent(ResourcePath("strings"), (ptr, characters) => {
					var span = new Span<byte>((byte*)ptr, characters * 2);
					string s = Encoding.Unicode.GetString(span);

					if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        res.Add(ResourcePath("strings") + "/" + s);

                    return true;
                });

			}
            return res.ToArray();
		}

		public static bool ResourceExists(string path) 
        {
            try 
            {
				ReadResource(path);
            }
            catch 
            {
                return false;
            }

            return true;
        }

        static string BasePath = "";        
        static string ResourcePath(string x) => x;

        public static bool HasDiskAccessPermission() 
        {
            if (!Program.Native.PlatformSupportsDiskAccess)
                return false;

            if (Program.Native.GetFileAccessInfo(out var canWrite, out _))
                return canWrite;
            else 
                Console.WriteLine("GetFileAccessInfo failed");

            return false;
        }

        public static bool CanRequestDiskAccessPermission() 
        {
            if (!Program.Native.PlatformSupportsDiskAccess)
                return false;

            if (Program.Native.GetFileAccessInfo(out _, out var canRequest))
                return canRequest;
            else
                Console.WriteLine("GetFileAccessInfo failed");

            return false;
        }

        public static void RequestDiskAccessPermission()
        {
            if (!Program.Native.PlatformSupportsDiskAccess)
                throw new NotImplementedException();

            if (!Program.Native.GetFileAccessInfo(out var hasPermission, out var canRequest))
                throw new NotImplementedException();

            if (hasPermission)
                return;

            if (!canRequest)
                return;

            Program.Native.RequestFileAccess();
        }
        
        private static string? _settingsStorePath = null;
        public static string SettingsStorePath()
        {
            if (_settingsStorePath is null)
                _settingsStorePath = Program.Native.GetSettingsStoragePath?.Invoke() ?? "";

            return _settingsStorePath;
        }

#else
		static string BasePath = Path.Combine(AppContext.BaseDirectory, "runtimes");
		static string ResourcePath(string x) => Path.Combine(BasePath, "resources", x);

		public static string? SettingsStorePath()
		{
			string? path = null;
			if (Program.IsContainerApp)
			{
				// https://github.com/exelix11/SysDVR/issues/255
				if (Program.IsLinux)
				{
					path = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
					if (string.IsNullOrWhiteSpace(path))
						path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sysdvr");
				}
			}
			else
				path = AppContext.BaseDirectory;

			return path;
		}

		public static byte[] ReadResource(string path) => File.ReadAllBytes(path);
		public static bool ResourceExists(string path) => File.Exists(path);

		public static bool HasDiskAccessPermission() => true;
		public static bool CanRequestDiskAccessPermission() => true;
		public static void RequestDiskAccessPermission() { }

        static string[] GetTranslationFiles()
        {
            if (!Directory.Exists(ResourcePath("strings")))
                return [];

            return Directory.GetFiles(ResourcePath("strings"), "*.json");
		}
#endif
		public static string RuntimesFolder => BasePath;
		public static string MainFont { get; private set; } = ResourcePath("fonts/OpenSans.ttf");
		public static string LoadingImage => ResourcePath("loading.yuv");

		public readonly static LazyImage Logo = new LazyImage(ResourcePath("logo.png"));
		public readonly static LazyImage UsbIcon = new LazyImage(ResourcePath("ico_usb.png"));
		public readonly static LazyImage WifiIcon = new LazyImage(ResourcePath("ico_wifi.png"));


        public static StringTableMetadata[] GetAvailableTranslations()
        {
            var files = GetTranslationFiles();
			var result = new List<StringTableMetadata>();

			foreach (var file in files)
            {
                try
                {
                    var table = JsonSerializer.Deserialize<StringTableMetadata>(ReadResource(file), StringTableSerializer.Default.SysDVRStringTableMetadata);
                    if (table is null)
                        continue;

                    if (table.SystemLocale.Length == 0)
                    {
                        Console.WriteLine($"Translation {table.TranslationName} was not loaded due to a missing system locale");
                        continue;
                    }

					table.FileName = file;
					result.Add(table);
				}
                catch (Exception ex)
                {
                    Program.DebugLog($"Failed to load translation {file} : {ex}");    
                }
			}

			return result.ToArray();
        }

        public static StringTable? LoadTranslationFromAssetName(string fullAssetName)
        {
            return JsonSerializer.Deserialize<StringTable>(ReadResource(fullAssetName), StringTableSerializer.Default.SysDVRStringTable) 
                ?? throw new Exception($"Failed to deserialize {fullAssetName}");
        }

        public static bool OverrideMainFont(string fontName)
        {
            var font = ResourcePath(fontName);
            if (!ResourceExists(font))
            {
				return false;
			}

			MainFont = font;
			return true;
        }
	}
}
