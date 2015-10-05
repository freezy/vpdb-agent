﻿using IniParser;
using IniParser.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ReactiveUI;
using VpdbAgent.PinballX.Models;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Splat;

namespace VpdbAgent.PinballX
{
	/// <summary>
	/// Manages PinballX's menu structure.
	/// 
	/// When initialized, watches PinballX.ini as well as all .XML files
	/// that are referenced in it for changes.
	/// </summary>
	/// 
	/// <remarks>
	/// Games are stored in the systems objects. This class really only does
	/// read-only maintenance of the user's configuration. See GameManager for
	/// data structures that are actually used in the application.
	/// </remarks>
	public class MenuManager : IMenuManager
	{
	
		/// <summary>
		/// Systems parsed from PinballX.ini.
		/// </summary>
		public ReactiveList<PinballXSystem> Systems { get; } = new ReactiveList<PinballXSystem>();

		// game change handlers
		private readonly Subject<PinballXSystem> _gamesChanged = new Subject<PinballXSystem>();
		public IObservable<PinballXSystem> GamesChanged => _gamesChanged.AsObservable();

		// dependencies
		private readonly IFileSystemWatcher _watcher;
		private readonly ISettingsManager _settingsManager;
		private readonly Logger _logger;

		public MenuManager(IFileSystemWatcher fileSystemWatcher, ISettingsManager settingsManager, Logger logger)
		{
			_watcher = fileSystemWatcher;
			_settingsManager = settingsManager;
			_logger = logger;
		}

		/// <summary>
		/// Starts watching file system for configuration changes and triggers an
		/// initial update.
		/// </summary>
		/// <returns></returns>
		public IMenuManager Initialize()
		{
			if (!_settingsManager.IsInitialized()) {
				return this;
			}

			var iniPath = _settingsManager.PbxFolder + @"\Config\PinballX.ini";
			var dbPath = _settingsManager.PbxFolder + @"\Databases\";

			ParseSystems(iniPath);

			// setup watchers
			_watcher.FileWatcher(iniPath)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(ParseSystems);
			_watcher.DatabaseWatcher(dbPath, Systems)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(path => ParseGames(Path.GetDirectoryName(path)));

			// parse all games when systems change
			Systems.Changed
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => ParseGames());

			// kick off initially parse
			ParseGames();

			return this;
		}

		/// <summary>
		/// Parses PinballX.ini and reads all systems from it.
		/// </summary>
		private void ParseSystems(string iniPath)
		{
			// only notify after this block
			using (Systems.SuppressChangeNotifications()) {
				_logger.Info("Parsing systems from {0}", iniPath);
				Systems.Clear();
				if (File.Exists(iniPath)) {
					var parser = new FileIniDataParser();
					var data = parser.ReadFile(iniPath);
					Systems.Add(new PinballXSystem(VpdbAgent.Models.Platform.PlatformType.VP, data["VisualPinball"]));
					Systems.Add(new PinballXSystem(VpdbAgent.Models.Platform.PlatformType.FP, data["FuturePinball"]));
					for (var i = 0; i < 20; i++) {
						if (data["System_" + i] != null) {
							Systems.Add(new PinballXSystem(data["System_" + i]));
						}
					}
				} else {
					_logger.Error("PinballX.ini at {0} does not exist.", iniPath);
				}
				_logger.Info("Done, {0} systems parsed.", Systems.Count);
			}
		}

		/// <summary>
		/// Parses all games for all systems.
		/// </summary>
		private void ParseGames()
		{
			_logger.Info("Parsing all games for all systems...");
			foreach (var system in Systems) {
				ParseGames(system.DatabasePath);
			}
		}

		/// <summary>
		/// Parses all games at a given path.
		/// </summary>
		/// <param name="path">Path to folder</param>
		private void ParseGames(string path)
		{
			_logger.Info("Parsing games at {0}", path);

			var system = Systems.FirstOrDefault(s => s.DatabasePath.Equals(path));

			if (system == null) {
				_logger.Warn("Unknown system at {0}, ignoring file change.", path);
				foreach (var s in Systems) {
					_logger.Warn("{0} != {1}", Path.GetDirectoryName(path), s.DatabasePath);
				}
				return;
			}
			var games = new List<Game>();
			var fileCount = 0;
			if (Directory.Exists(system.DatabasePath)) {
				foreach (var filePath in Directory.GetFiles(system.DatabasePath)
					.Where(filePath => "xml".Equals(filePath.Substring(filePath.Length - 3), StringComparison.InvariantCultureIgnoreCase))) {
					games.AddRange(UnmarshalXml(filePath).Games);
					fileCount++;
				}
			}
			_logger.Debug("Parsed {0} games from {1} XML files at {2}.", games.Count, fileCount, system.DatabasePath);
			system.Games = games;

			// announce to subscribers
			_gamesChanged.OnNext(system);
		}

		/// <summary>
		/// Returns an unmarshaled object for a given .XML file
		/// </summary>
		/// <param name="filepath">Absolute path to the .XML file</param>
		/// <returns></returns>
		private Menu UnmarshalXml(string filepath)
		{
			var menu = new Menu();
			Stream reader = null;
			try {
				var serializer = new XmlSerializer(typeof(Menu));
				reader = new FileStream(filepath, FileMode.Open);
				menu = serializer.Deserialize(reader) as Menu;

			} catch (Exception e) {
				_logger.Error(e, "Error parsing {0}: {1}", filepath, e.Message);

			} finally {
				reader?.Close();
			}
			return menu;
		}
	}

	public interface IMenuManager
	{
		ReactiveList<PinballXSystem> Systems { get; }
		IObservable<PinballXSystem> GamesChanged { get; }
		IMenuManager Initialize();
	}
}
