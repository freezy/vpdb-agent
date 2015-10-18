﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows;
using Newtonsoft.Json;
using NLog;
using PusherClient;
using ReactiveUI;
using VpdbAgent.Common;
using VpdbAgent.Models;
using VpdbAgent.PinballX;
using VpdbAgent.PinballX.Models;
using VpdbAgent.Vpdb;
using VpdbAgent.Vpdb.Models;
using VpdbAgent.Vpdb.Network;
using Game = VpdbAgent.Models.Game;

namespace VpdbAgent
{
	/// <summary>
	/// Our internal game API.
	/// 
	/// It manages the games that are defined in the user's XML database of
	/// PinballX. However, since we're dealing with more data than what's in
	/// those XMLs, we keep our own data structure in JSON files.
	/// 
	/// JSON files are system-specific, meaning for every system defined in
	/// PinballX (we call them "Platforms"), there is one JSON file. Games
	/// are always linked to the respective system so we know where to retrieve
	/// table files, media etc.
	/// 
	/// This is how the JSON files are generated:
	///   1. GameManager instantiates MenuManager which parses PinballX.ini
	///   2. GameManager loops through parsed systems and retrieves local vpdb.jsons
	///   3. GameManager merges games from MenuManager and vpdb.json to new vpdb.jsons
	///   4. GameManager dumps new vpdb.jsons
	/// 
	/// Everything is event- or subscription based, since we want to automatically
	/// repeat the process when relevant files change. 
	/// </summary>
	public class GameManager : IGameManager
	{
		// deps
		private readonly IMenuManager _menuManager;
		private readonly Logger _logger;

		// props
		public ReactiveList<Platform> Platforms { get; } = new ReactiveList<Platform>();
		public ReactiveList<Game> Games { get; } = new ReactiveList<Game>();

		public GameManager(IMenuManager menuManager, IVpdbClient vpdbClient, Logger logger)
		{
			_menuManager = menuManager;
			_logger = logger;

			var systems = _menuManager.Systems;

			// populate platforms when system change
			systems.Changed
				.ObserveOn(Scheduler.Default)
				.Subscribe(UpdatePlatforms);

			// populate platform when games change
			systems.Changed
				.ObserveOn(Scheduler.Default)
				.SelectMany(_ => systems
					.Select(parent => parent.Games.Changed.Skip(1).Select(__ => parent))
				.Merge())
			.Subscribe(UpdatePlatform);

			// here we push all games in all platforms into the Games list. See http://stackoverflow.com/questions/15254708/
			var whenPlatformsOrGamesInThosePlatformsChange = Observable.Merge(
				Platforms.Changed                                                      // one of the games changes
					.SelectMany(_ => Platforms.Select(x => x.Games.Changed).Merge())
					.Select(_ => Unit.Default),
				Platforms.Changed.Select(_ => Unit.Default));                          // one of the platforms changes

			whenPlatformsOrGamesInThosePlatformsChange.StartWith(Unit.Default)
				.Select(_ => Platforms.SelectMany(x => x.Games).ToList())
				.Where(games => games.Count > 0)
				.Subscribe(games => {
					// TODO better logic
					using (Games.SuppressChangeNotifications()) {
						Games.RemoveRange(0, Games.Count);
						Games.AddRange(games);
					}
					_logger.Info("Set {0} games.", games.Count);
				});

			// subscribe to pusher
			vpdbClient.UserChannel.Subscribe(OnChannelJoined);
		}

		private void UpdatePlatform(PinballXSystem system)
		{
			_logger.Info("Updating games for {0}", system);

			// create new platform and find old
			var newPlatform = new Platform(system);
			var oldPlatform = Platforms.FirstOrDefault(p => p.Name.Equals(system.Name));

			// save vpdb.json for updated platform
			newPlatform.Save();
			
			// update platforms back on main thread
			Application.Current.Dispatcher.Invoke(delegate {
				using (Platforms.SuppressChangeNotifications()) {
					if (oldPlatform != null) {
						Platforms.Remove(oldPlatform);
					}
					Platforms.Add(newPlatform);
				}
			});
		}

		private void UpdatePlatforms(NotifyCollectionChangedEventArgs args)
		{
			_logger.Info("Updating all games for all platforms");

			// create platforms from games
			var platforms = _menuManager.Systems.Select(system => new Platform(system)).ToList();

			// write vpdb.json
			platforms.ForEach(p => p.Save());

			// update platforms back on main thread
			Application.Current.Dispatcher.Invoke(delegate {
				using (Platforms.SuppressChangeNotifications()) {
					// todo make this more intelligent by diff'ing and changing instead of drop-and-create
					Platforms.Clear();
					Platforms.AddRange(platforms);
				}
			});
		}

		/// <summary>
		/// Triggers data update
		/// </summary>
		/// <returns>This instance</returns>
		public IGameManager Initialize()
		{
			_menuManager.Initialize();
			return this;
		}

		/// <summary>
		/// Links a game to a release at VPDB and saves the database.
		/// </summary>
		/// <param name="game">Local game to link</param>
		/// <param name="release">Release at VPDB</param>
		public IGameManager LinkRelease(Game game, Release release)
		{
			game.Release = release;
			game.Platform.Save();
			return this;
		}

		/// <summary>
		/// Executed when the pusher connection with the private user channel
		/// is established and we can subscribe to messages.
		/// </summary>
		/// <param name="userChannel">User channel object</param>
		private void OnChannelJoined(Channel userChannel)
		{
			if (userChannel == null) {
				return;
			}

			// subscribe through a subject so we can do more fun stuff with it
			var star = new Subject<dynamic>();
			var unstar = new Subject<dynamic>();
			userChannel.Bind("star", data => star.OnNext(data));
			userChannel.Bind("unstar", data => unstar.OnNext(data));

			// star
			star.ObserveOn(RxApp.MainThreadScheduler).Subscribe(data =>
			{
				if ("release".Equals((string)data.type)) {
					var release = FindRelease((string)data.id);
					if (release != null) {
						release.Starred = true;
						_logger.Info("Toggled star on release {0} [on]", release.Name);
					} else {
						_logger.Info("Ignoring star for id {0}", data.id);
					}
				}
			});

			// unstar
			unstar.ObserveOn(RxApp.MainThreadScheduler).Subscribe(data =>
			{
				if ("release".Equals((string)data.type)) {
					var release = FindRelease((string)data.id);
					if (release != null) {
						release.Starred = false;
						_logger.Info("Toggled star on release {0} [off]", release.Name);
					} else {
						_logger.Info("Ignoring star for id {0}", data.id);
					}
				}
			});
		}

		/// <summary>
		/// Returns a release based on release ID or null if not locally found.
		/// </summary>
		/// <param name="releaseId">Release ID</param>
		/// <returns></returns>
		private Release FindRelease(string releaseId)
		{
			return Games
				.Where(game => game.HasRelease && game.Release.Id.Equals(releaseId))
				.Select(game => game.Release)
				.FirstOrDefault();
		}
	}

	public interface IGameManager
	{
		ReactiveList<Platform> Platforms { get; }
		ReactiveList<Game> Games { get; }

		IGameManager Initialize();
		IGameManager LinkRelease(Game game, Release release);
	}
}