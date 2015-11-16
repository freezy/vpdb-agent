﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using ReactiveUI;
using VpdbAgent.Application;
using VpdbAgent.Vpdb.Models;
using File = VpdbAgent.Vpdb.Models.File;

namespace VpdbAgent.Vpdb
{

	/// <summary>
	/// Manages a download queue supporting parallel processing of downloads
	/// on multiple worker threads.
	/// </summary>
	public interface IDownloadManager
	{
		/// <summary>
		/// Downloads or upgrades a release, including table image, ROMs and other 
		/// media, based on the user's settings.
		/// </summary>
		/// <remarks>
		/// File selection is based on the user's preferences. This is typically
		/// called when the user stars a release, where we have no more data
		/// than the release. 
		/// </remarks>
		/// <param name="releaseId">ID of the release</param>
		/// <returns>This instance</returns>
		IDownloadManager DownloadRelease(string releaseId);

		/// <summary>
		/// Downloads or upgrades a release, including table image, ROMs and other
		/// media, based on the user's settings.
		/// </summary>
		/// <param name="release">Release to download or upgrade</param>
		/// <param name="file">File of the release to download</param>
		/// <returns>This instance</returns>
		IDownloadManager DownloadRelease(Release release, File file);

		/// <summary>
		/// Deletes a job from the database.
		/// </summary>
		/// <param name="job"></param>
		/// <returns></returns>
		IDownloadManager DeleteJob(DownloadJob job);

		/// <summary>
		/// Retries a failed or aborted job.
		/// </summary>
		/// <param name="job"></param>
		/// <returns></returns>
		IDownloadManager RetryJob(DownloadJob job);

		/// <summary>
		/// An observable that produces values as when any job in the queue
		/// (active or not) changes status.
		/// </summary>
		IObservable<DownloadJob.JobStatus> WhenStatusChanged { get; }

		/// <summary>
		/// An observable that produces a value when a job finished downloading
		/// successfully.
		/// </summary>
		IObservable<DownloadJob> WhenDownloaded { get; }

		/// <summary>
		/// A list of all current, future and past jobs (i.e. independent of their 
		/// status)
		/// </summary>
		ReactiveList<DownloadJob> CurrentJobs { get; }
	}

	/// <summary>
	/// Application logic for <see cref="IDownloadManager"/>.
	/// </summary>
	public class DownloadManager : IDownloadManager
	{
		// increase this on demand...
		public static readonly int MaximalSimultaneousDownloads = 2;

		// dependencies
		private readonly IVpdbClient _vpdbClient;
		private readonly IDatabaseManager _databaseManager;
		private readonly ISettingsManager _settingsManager;
		private readonly Logger _logger;

		// props
		public ReactiveList<DownloadJob> CurrentJobs { get; }
		public IObservable<DownloadJob.JobStatus> WhenStatusChanged => _whenStatusChanged;
		public IObservable<DownloadJob> WhenDownloaded => _whenDownloaded;

		// members
		private readonly Subject<DownloadJob> _jobs = new Subject<DownloadJob>();
		private readonly Subject<DownloadJob.JobStatus> _whenStatusChanged = new Subject<DownloadJob.JobStatus>();
		private readonly Subject<DownloadJob> _whenDownloaded = new Subject<DownloadJob>();
		private readonly IDisposable _queue;
		private readonly string _downloadPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
			SettingsManager.DataFolder,
			"Download"
		);
		private readonly List<IFlavorMatcher> _flavorMatchers = new List<IFlavorMatcher>();

		/// <summary>
		/// Constructor sets up queue and creates download folder if non-existing.
		/// </summary>
		public DownloadManager(IVpdbClient vpdbClient, IDatabaseManager databaseManager, ISettingsManager settingsManager, Logger logger)
		{
			CurrentJobs = databaseManager.Database.DownloadJobs;

			_vpdbClient = vpdbClient;
			_databaseManager = databaseManager;
			_settingsManager = settingsManager;
			_logger = logger;

			// setup transfer queue
			_queue = _jobs
				.ObserveOn(Scheduler.Default)
				.Select(job => Observable.DeferAsync(async token => Observable.Return(await ProcessDownload(job, token))))
				.Merge(MaximalSimultaneousDownloads)
				.Subscribe(job => {
					_databaseManager.Save();

					if (job.Status != DownloadJob.JobStatus.Aborted) {
						_whenDownloaded.OnNext(job);
					}
				}, error => {
					_databaseManager.Save();
					// todo treat error in ui
					_logger.Error(error, "Error: {0}", error.Message);
				});

			if (!Directory.Exists(_downloadPath)) {
				_logger.Info("Creating non-existing download folder at {0}.", _downloadPath);
				Directory.CreateDirectory(_downloadPath);
			}

			// setup flavor matchers as soon as settings are available.
			_settingsManager.Settings.WhenAny(
				s => s.DownloadOrientation,
				s => s.DownloadOrientationFallback,
				s => s.DownloadLighting,
				s => s.DownloadLightingFallback,
				(a, b, c, d) => Unit.Default).Subscribe(_ => {
					_logger.Info("Setting up flavor matchers.");
					_flavorMatchers.Clear();
					_flavorMatchers.Add(new OrientationMatcher(_settingsManager.Settings));
					_flavorMatchers.Add(new LightingMatcher(_settingsManager.Settings));
				});
		}

		public IDownloadManager DownloadRelease(string id)
		{
			// retrieve release details
			_logger.Info("Retrieving details for release {0}...", id);
			_vpdbClient.Api.GetRelease(id)
				.ObserveOn(Scheduler.Default)
				.Subscribe(release => {

					// match file based on settings
					var file = release.Versions
						.SelectMany(v => v.Files)
						.Where(FlavorMatches)
						.Select(f => new { f, weight = FlavorWeight(f) })
						.OrderBy(x => x.weight)
						.Select(x => x.f)
						.LastOrDefault();

					// check if match
					if (file == null) {
						_logger.Info("Nothing matched current flavor configuration, skipping.");
						return;
					}

					// download
					DownloadRelease(release, file);

				}, exception => _vpdbClient.HandleApiError(exception, "retrieving release details during download"));

			return this;
		}

		public IDownloadManager DownloadRelease(Release release, File file)
		{
			// todo also queue all remaining non-table files of the release.
			// todo also queue media & roms if available, based on settings

			// queue for download
			var job = new DownloadJob(release, file);
			AddToCurrentJobs(job);
			_jobs.OnNext(job);

			return this;
		}

		public IDownloadManager RetryJob(DownloadJob job)
		{
			System.Windows.Application.Current.Dispatcher.Invoke(delegate
			{
				job.Initialize();
				job.WhenStatusChanges.Subscribe(status => _whenStatusChanged.OnNext(status));
				_databaseManager.Save();
				_jobs.OnNext(job);
			});
			return this;
		}

		public IDownloadManager DeleteJob(DownloadJob job)
		{
			// update jobs back on main thread
			System.Windows.Application.Current.Dispatcher.Invoke(delegate
			{
				_databaseManager.Database.DownloadJobs.Remove(job);
				_databaseManager.Save();
			});
			return this;
		}

		public void CancelAll()
		{
			_queue.Dispose();
		}

		/// <summary>
		/// Starts the download.
		/// </summary>
		/// <remarks>
		/// This is called when the queue has a new download slot available.
		/// </remarks>
		/// <param name="job">Job to start</param>
		/// <param name="token">Cancelation token</param>
		/// <returns>Job</returns>
		private async Task<DownloadJob> ProcessDownload(DownloadJob job, CancellationToken token)
		{
			var dest = Path.Combine(_downloadPath, job.FileName);

			_logger.Info("Starting downloading of {0} to {1}", job.Uri, dest);

			// setup cancelation
			token.Register(job.Client.CancelAsync);

			// update statuses
			job.OnStart(token, dest);

			// do the grunt work
			try {
				await job.Client.DownloadFileTaskAsync(job.Uri, dest);
				job.OnSuccess();
				_logger.Info("Finished downloading of {0}", job.Uri);
			} catch (WebException e) {
				if (e.Status == WebExceptionStatus.RequestCanceled) {
					job.OnCancelled();
				} else {
					job.OnFailure(e);
					_logger.Error(e, "Error downloading file (server error): {0}", e.Message);
				}
			} catch (Exception e) {
				job.OnFailure(e);
				_logger.Error(e, "Error downloading file: {0}", e.Message);
			}
			return job;
		}

		/// <summary>
		/// Persists a new job and forwards its changing status to the
		/// global one.
		/// </summary>
		/// <param name="job">Job to add</param>
		private void AddToCurrentJobs(DownloadJob job)
		{
			// update jobs back on main thread
			System.Windows.Application.Current.Dispatcher.Invoke(delegate
			{
				job.WhenStatusChanges.Subscribe(status => _whenStatusChanged.OnNext(status));
				_databaseManager.Database.DownloadJobs.Add(job);
				_databaseManager.Save();
			});
		}

		/// <summary>
		/// Checks if the flavor of the file is acceptable by the user's flavor settings.
		/// </summary>
		/// <param name="file">File to check</param>
		/// <returns>Returns true if the primary or secondary flavor setting of ALL flavors matches, false otherwise.</returns>
		private bool FlavorMatches(File file)
		{
			return _flavorMatchers.TrueForAll(matcher => matcher.Matches(file));
		}

		/// <summary>
		/// Calculates the total weight of a file based on flavor settings.
		/// </summary>
		/// <param name="file">File to check</param>
		/// <returns>Total weight of the file based on the user's flavor settings</returns>
		private int FlavorWeight(File file)
		{
			return _flavorMatchers.Sum(matcher => matcher.Weight(file));
		}
	}
}
