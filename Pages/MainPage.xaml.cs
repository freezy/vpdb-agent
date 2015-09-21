﻿using NLog;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace VpdbAgent.Pages
{
	/// <summary>
	/// Interaction logic for MainPage.xaml
	/// </summary>
	public partial class MainPage : Page
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		public ObservableCollection<Models.Platform> Platforms { get; private set; }
		public ObservableCollection<Models.Game> Games { get; private set; }

		//public MenuManager MenuManager { get; set; }

		public MainPage()
		{
			InitializeComponent();
			DataContext = this;

			GameManager gameManager = GameManager.GetInstance();
			gameManager.Initialize();

			Platforms = gameManager.Platforms;
			Games = new ObservableCollection<Models.Game>(gameManager.GetGames());

//			Platforms = gameManager.Platforms.Where(platform => { return platform.Enabled; });
		}

		/*
		private async void getReleases()
		{

			VpdbClient client = new VpdbClient();

			try {
				Releases = await client.Api.GetReleases();
				GamesList.ItemsSource = Releases;
				foreach (Release release in Releases) {
					Console.WriteLine("{0} - {1} ({2})", release.Game.Title, release.Name, release.Id);
				}
			} catch (Exception e) {
				Console.WriteLine("Error retrieving releases: {0}", e.Message);
			}
		}*/
	}
}
