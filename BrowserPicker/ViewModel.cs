﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using JetBrains.Annotations;
using Microsoft.Win32;

namespace BrowserPicker
{
	public class ViewModel : INotifyPropertyChanged
	{
		// Used by WPF designer
		[UsedImplicitly]
		public ViewModel() : this(true)
		{
		}

		public ViewModel(bool forceChoice)
		{
			ConfigurationMode = App.TargetURL == null;
			Configuration = new Config();

			Choices = new ObservableCollection<Browser>(Configuration.BrowserList);
			if (Choices.Count == 0)
				FindBrowsers();

			if (Configuration.AlwaysPrompt || ConfigurationMode || forceChoice)
				return;

			if (App.TargetURL != null)
				CheckDefaultBrowser();

			//var active = Choices.Where(b => b.IsRunning).ToList();
			//if (active.Count == 1)
			//	active[0].Select.Execute(null);
		}

		private void CheckDefaultBrowser()
		{
			var defaults = Configuration.Defaults.ToList();
			if (defaults.Count <= 0)
				return;

			var url = new Uri(App.TargetURL);
			var auto = defaults
				.Select(rule => new { rule, matchLength = rule.MatchLength(url)})
				.Where(o => o.matchLength > 0)
				.ToList();
			if (auto.Count <= 0)
				return;

			var browser = auto.OrderByDescending(o => o.matchLength).First().rule.Browser;
			var start = Choices.FirstOrDefault(c => c.Name == browser);
			if(start == null || Configuration.DefaultsWhenRunning && !start.IsRunning)
				return;

			start.Select.Execute(null);
		}


		public ICommand RefreshBrowsers => new DelegateCommand(FindBrowsers);

		public ICommand Configure => new DelegateCommand(() => {
			ConfigurationMode = !ConfigurationMode;
			if (!ConfigurationMode)
			{
				Configuration.BrowserList = Choices;
			}
		});

		public ICommand Exit => new DelegateCommand(() => Application.Current.Shutdown());

		public DelegateCommand AddBrowser => new DelegateCommand(AddBrowserManually);

		private void AddBrowserManually()
		{
			var editor = new BrowserEditor();
			editor.Show();
			editor.Closing += Editor_Closing;
		}

		private void Editor_Closing(object sender, CancelEventArgs e)
		{
			((Window)sender).Closing -= Editor_Closing;
			if (!(((Window)sender).DataContext is Browser browser))
				return;
			Choices.Add(browser);
			Configuration.BrowserList = Choices;
		}

		public Config Configuration { get; }

		public ObservableCollection<Browser> Choices { get; }

		public bool ConfigurationMode
		{
			get => configuration_mode;
			set
			{
				configuration_mode = value;
				OnPropertyChanged();
			}
		}

		public string TargetURL => App.TargetURL;

		private void FindBrowsers()
		{
			var removed = Choices.Where(b => b.Removed).ToList();
			if (removed.Count > 0)
				removed.ForEach(b => Choices.Remove(b));

			EnumerateBrowsers(@"SOFTWARE\Clients\StartMenuInternet");
			EnumerateBrowsers(@"SOFTWARE\WOW6432Node\Clients\StartMenuInternet");
			FindEdge();
			Configuration.BrowserList = Choices;
		}

		private void FindEdge()
		{
			if (Choices.Any(b => b.Name.Equals("Edge")))
				return;

			var systemApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps");
			if (!Directory.Exists(systemApps))
				return;

			var targets = Directory.GetDirectories(systemApps, "*MicrosoftEdge_*");
			if (targets.Length > 0)
			{
				Choices.Add(
					new Browser
					{
						Name = "Edge",
						Command = $"shell:AppsFolder\\{Path.GetFileName(targets[0])}!MicrosoftEdge",
						IconPath = Path.Combine(targets[0], "Assets", "MicrosoftEdgeSquare44x44.targetsize-32_altform-unplated.png")
					}
				);
			}
		}

		private void EnumerateBrowsers(string subKey)
		{
			var root = Registry.LocalMachine.OpenSubKey(subKey, false);
			if (root == null)
				return;
			foreach (var browser in root.GetSubKeyNames().Where(n => n != "BrowserPicker"))
				GetBrowserDetails(root, browser);
		}

		private void GetBrowserDetails(RegistryKey root, string browser)
		{
			var reg = root.OpenSubKey(browser, false);
			if (reg == null)
				return;

			var name = (string)reg.GetValue(null);
			if (Choices.Any(c => c.Name == name))
				return;

			var icon = (string)reg.OpenSubKey("DefaultIcon", false)?.GetValue(null);
			var shell = (string)reg.OpenSubKey("shell\\open\\command", false)?.GetValue(null);
			if (icon?.Contains(",") ?? false)
				icon = icon.Split(',')[0];
			Choices.Add(
				new Browser
				{
					Name = name,
					IconPath = icon,
					Command = shell
				}
			);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		[NotifyPropertyChangedInvocator]
		private void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private bool configuration_mode;

		public void OnDeactivated()
		{
			if (!ConfigurationMode)
				Application.Current.Shutdown();
		}
	}
}
