﻿/*
 * Copyright (C) 2021 Patrick Mours
 * SPDX-License-Identifier: BSD-3-Clause
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;

static class StringExtensionMethods
{
	public static bool ContainsIgnoreCase(this string s, string value)
	{
		return s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
	}
}

static class HashSetExtensionMethods
{
	public static T Dequeue<T>(this HashSet<T> items)
	{
		if (items.Count == 0)
		{
			throw new InvalidOperationException();
		}

		var item = items.First();
		items.Remove(item);
		return item;
	}
}

namespace ReShade.Setup.Pages
{
	public partial class SelectAppPage : Page
	{
		class ProgramItem
		{
			public ProgramItem(string path, FileVersionInfo info)
			{
				Path = path;
				Name = info.FileDescription;
				if (Name is null || Name.Trim().Length == 0)
				{
					Name = System.IO.Path.GetFileNameWithoutExtension(path);
					if (char.IsLower(Name[0]))
					{
						Name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Name);
					}
				}

				Name += " (" + System.IO.Path.GetFileName(path) + ")";

				using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
				{
					Icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
				}

				try
				{
					LastAccess = File.GetLastAccessTime(path).ToString("s");
				}
				catch
				{
					LastAccess = string.Empty;
				}
			}

			public string Name { get; }
			public string Path { get; }
			public ImageSource Icon { get; }
			public string LastAccess { get; }
		}

		Thread UpdateThread = null;
		AutoResetEvent SuspendUpdateThreadEvent = new AutoResetEvent(false);
		bool SuspendUpdateThread = false;
		bool IgnorePathBoxChanged = false;
		public string FileName { get => PathBox.Text; set => PathBox.Text = value; }
		ObservableCollection<ProgramItem> ProgramListItems = new ObservableCollection<ProgramItem>();

		public SelectAppPage()
		{
			InitializeComponent();

			ProgramList.ItemsSource = CollectionViewSource.GetDefaultView(ProgramListItems);

			UpdateThread = new Thread(() =>
			{
				var files = new List<string>();
#if !RESHADE_SETUP_USE_MUI_CACHE
				var searchPaths = new HashSet<string>();

				// Add Steam install locations
				try
				{
					string steamInstallPath = Registry.LocalMachine.OpenSubKey(@"Software\Wow6432Node\Valve\Steam")?.GetValue("InstallPath") as string;
					if (!string.IsNullOrEmpty(steamInstallPath) && Directory.Exists(steamInstallPath))
					{
						searchPaths.Add(Path.Combine(steamInstallPath, "steamapps", "common"));

						string steamConfig = File.ReadAllText(Path.Combine(steamInstallPath, "config", "libraryfolders.vdf"));
						foreach (Match match in new Regex("\"path\"\\s+\"(.+)\"").Matches(steamConfig))
						{
							searchPaths.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common"));
						}
					}
				}
				catch { }

				// Add Origin install locations
				try
				{
					string originConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Origin", "local.xml");
					if (File.Exists(originConfigPath))
					{
						var originConfig = new XmlDocument();
						originConfig.Load(originConfigPath);

						foreach (string searchPath in originConfig["Settings"].ChildNodes.Cast<XmlNode>()
							.Where(x => x.Attributes["key"].Value == "DownloadInPlaceDir")
							.Select(x => x.Attributes["value"].Value))
						{
							// Avoid adding short paths to the search paths so not to scan the entire drive
							if (searchPath.Length > 25)
							{
								searchPaths.Add(searchPath);
							}
						}
					}
				}
				catch { }

				// Add Epic Games Launcher install location
				{
					string epicGamesInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
					if (Directory.Exists(epicGamesInstallPath))
					{
						searchPaths.Add(epicGamesInstallPath);
					}
				}

				// Add GOG Galaxy install locations
				try
				{
					var gogGamesKey = Registry.LocalMachine.OpenSubKey(@"Software\Wow6432Node\GOG.com\Games");
					if (gogGamesKey != null)
					{
						foreach (var gogGame in gogGamesKey.GetSubKeyNames())
						{
							string gameDir = gogGamesKey.OpenSubKey(gogGame)?.GetValue("path") as string;
							if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
							{
								searchPaths.Add(gameDir);
							}
						}
					}
				}
				catch { }
#else
				foreach (var name in new string[] {
					"Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\MuiCache",
					"Software\\Microsoft\\Windows\\ShellNoRoam\\MUICache",
					"Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Compatibility Assistant\\Persisted",
					"Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Compatibility Assistant\\Store" })
				{
					try
					{
						string[] values = Registry.CurrentUser.OpenSubKey(name)?.GetValueNames();
						if (values != null)
						{
							files.AddRange(values);
						}
					}
					catch
					{
						// Ignore permission errors
						continue;
					}
				}
#endif

				const int SPLIT_SIZE = 50;
				var items = new KeyValuePair<string, FileVersionInfo>[SPLIT_SIZE];

#if !RESHADE_SETUP_USE_MUI_CACHE
				while (searchPaths.Count != 0)
				{
					string searchPath = searchPaths.Dequeue();

					try
					{
						files = Directory.GetFiles(searchPath, "*.exe", SearchOption.TopDirectoryOnly).ToList();
					}
					catch
					{
						// Skip permission errors
						continue;
					}
#endif

					for (int offset = 0; offset < files.Count; offset += SPLIT_SIZE)
					{
						if (SuspendUpdateThread)
						{
							SuspendUpdateThreadEvent.WaitOne();
						}

						for (int i = 0; i < Math.Min(SPLIT_SIZE, files.Count - offset); ++i)
						{
							string path = files[offset + i];

							if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
							{
								continue;
							}

							// Exclude common installer, support and launcher executables
							if (path.ContainsIgnoreCase("redis") ||
								path.ContainsIgnoreCase("unins") ||
								path.ContainsIgnoreCase("setup") ||
								path.ContainsIgnoreCase("patch") ||
								path.ContainsIgnoreCase("update") ||
								path.ContainsIgnoreCase("install") ||
								path.ContainsIgnoreCase("report") ||
								path.ContainsIgnoreCase("support") ||
								path.ContainsIgnoreCase("register") ||
								path.ContainsIgnoreCase("activation") ||
								path.ContainsIgnoreCase("diagnostics") ||
								path.ContainsIgnoreCase("tool") ||
								path.ContainsIgnoreCase("crash") ||
								path.ContainsIgnoreCase("config") ||
								path.ContainsIgnoreCase("launch") ||
								path.ContainsIgnoreCase("plugin") ||
								path.ContainsIgnoreCase("benchmark") ||
								path.ContainsIgnoreCase("steamvr") ||
								path.ContainsIgnoreCase("cefprocess") ||
								path.Contains("svc") ||
								// Ignore certain folders that are unlikely to contain useful executables
								path.ContainsIgnoreCase("docs") ||
								path.ContainsIgnoreCase("cache") ||
								path.ContainsIgnoreCase("support") ||
								path.Contains("Data") || // AppData, ProgramData, _Data
								path.Contains("_CommonRedist") ||
								path.Contains("__Installer") ||
								path.Contains("\\$") ||
								path.Contains("\\.") ||
								path.Contains("\\Windows") ||
								path.ContainsIgnoreCase("steamvr"))
							{
								continue;
							}

							items[i] = new KeyValuePair<string, FileVersionInfo>(path, FileVersionInfo.GetVersionInfo(path));
						}

						Dispatcher.Invoke(new Action<KeyValuePair<string, FileVersionInfo>[]>(arg =>
						{
							foreach (var item in arg)
							{
								if (item.Key == null || ProgramListItems.Any(x => x.Path == item.Key))
								{
									continue;
								}

								ProgramListItems.Add(new ProgramItem(item.Key, item.Value));
							}
						}), DispatcherPriority.Background, (object)items);

						// Give back control to the OS
						Thread.Sleep(5);
					}

#if !RESHADE_SETUP_USE_MUI_CACHE
					// Continue searching in sub-directories
					foreach (var path in Directory.GetDirectories(searchPath))
					{
						searchPaths.Add(path);
					}
				}
#endif
			});

			UpdateThread.Start();
		}

		public void Cancel()
		{
			UpdateThread.Abort();
		}

		private void OnBrowseClick(object sender, RoutedEventArgs e)
		{
			SuspendUpdateThread = true;

			var dlg = new OpenFileDialog
			{
				Filter = "Applications|*.exe",
				DefaultExt = ".exe",
				Multiselect = false,
				ValidateNames = true,
				CheckFileExists = true
			};

			if (dlg.ShowDialog() == true)
			{
				UpdateThread.Abort();

				PathBox.Focus();
				FileName = dlg.FileName;
			}
			else
			{
				SuspendUpdateThread = false;
				SuspendUpdateThreadEvent.Set();
			}
		}

		private void OnPathGotFocus(object sender, RoutedEventArgs e)
		{
			Dispatcher.BeginInvoke(new Action(PathBox.SelectAll));
		}

		private void OnPathTextChanged(object sender, TextChangedEventArgs e)
		{
			if (IgnorePathBoxChanged)
			{
				return;
			}

			OnSortByChanged(sender, null);

			if (PathBox.IsFocused)
			{
				ProgramList.UnselectAll();
			}
		}

		private void OnSortByChanged(object sender, SelectionChangedEventArgs e)
		{
			var view = CollectionViewSource.GetDefaultView(ProgramListItems);

			if (PathBox != null && PathBox.Text != "Search" && !Path.IsPathRooted(PathBox.Text))
			{
				view.Filter = item => ((ProgramItem)item).Path.ContainsIgnoreCase(PathBox.Text) || ((ProgramItem)item).Name.ContainsIgnoreCase(PathBox.Text);
			}
			else
			{
				view.Filter = null;
			}

			view.SortDescriptions.Clear();
			switch (SortBy.SelectedIndex)
			{
				case 0:
					view.SortDescriptions.Add(new SortDescription(nameof(ProgramItem.LastAccess), ListSortDirection.Descending));
					break;
				case 1:
					view.SortDescriptions.Add(new SortDescription(nameof(ProgramItem.Name), ListSortDirection.Ascending));
					break;
				case 2:
					view.SortDescriptions.Add(new SortDescription(nameof(ProgramItem.Name), ListSortDirection.Descending));
					break;
			}
		}

		private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (ProgramList.SelectedItem is ProgramItem item)
			{
				IgnorePathBoxChanged = true;
				FileName = item.Path;
				IgnorePathBoxChanged = false;

				ProgramList.ScrollIntoView(item);
			}
		}
	}
}
