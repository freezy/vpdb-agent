﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NLog;
using VpdbAgent.Common;
using VpdbAgent.Vpdb;
using VpdbAgent.Vpdb.Models;
using Game = VpdbAgent.Models.Game;

namespace VpdbAgent.Controls
{
	/// <summary>
	/// Interaction logic for GameTemplate.xaml
	/// </summary>
	public partial class GameTemplate : UserControl
	{
		public static readonly DependencyProperty GameProperty = DependencyProperty.Register("Game", typeof(Game), typeof(GameTemplate), new PropertyMetadata(default(Game), GamePropertyChanged));
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private readonly GameManager _gameManager = GameManager.GetInstance();
		private readonly VpdbClient _vpdbClient = VpdbClient.GetInstance();
		private readonly ImageUtils _imageUtils = ImageUtils.GetInstance();

		static void GamePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var source = d as GameTemplate;
			source?.Bind();
		}

		public Game Game
		{
			get { return GetValue(GameProperty) as Game; }
			set { SetValue(GameProperty, value); }
		}

		public List<Release> IdentifiedReleases { get; private set; }

		public GameTemplate()
		{
			InitializeComponent();
		}

		private void Bind()
		{

			if (Game.HasRelease) {
				ReleaseNameWrapper.Visibility = Visibility.Visible;
				ReleaseName.Visibility = Visibility.Visible;
				ReleaseName.Text = Game.Release.Name;

				Thumb.Visibility = Visibility.Visible;
				_imageUtils.LoadImage(Game.Release.LatestVersion.Thumb.Image.Url, Thumb, Dispatcher);
				IdentifyButton.Visibility = Visibility.Collapsed;

			} else {
				ReleaseNameWrapper.Visibility = Visibility.Collapsed;
				ReleaseName.Visibility = Visibility.Collapsed;
				Thumb.Visibility = Visibility.Collapsed;
				IdentifyButton.Visibility = Visibility.Visible;
			}
			Filename.Background = Game.Exists ? Brushes.Transparent : Brushes.DarkRed;
			IdentifyButton.IsEnabled = Game.Exists;
		}

		private async void IdentifyButton_Click(object sender, RoutedEventArgs e)
		{
			IdentifyButton.Visibility = Visibility.Collapsed;
			Progress.Visibility = Visibility.Visible;
			Progress.Start();
			
			var releases = await _vpdbClient.Api.GetReleasesBySize(Game.FileSize, 512);

			Progress.Visibility = Visibility.Collapsed;
			Progress.Stop();

			var identifyResults = new ReleaseIdentifyResultsTemplate(releases);
			Panel.Children.Add(identifyResults);
			identifyResults.IdentifyResults.IsExpanded = true;

			// TODO handle # results correctly
			if (releases.Count > 0) {
				//				_gameManager.LinkRelease(Game, releases[0]);
			}

			Logger.Info("Found {0} matches.", releases.Count);
		}

	}
}
