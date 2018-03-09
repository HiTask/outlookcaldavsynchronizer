﻿// This file is Part of CalDavSynchronizer (http://outlookcaldavsynchronizer.sourceforge.net/)
// Copyright (c) 2015 Gerhard Zehetbauer
// Copyright (c) 2015 Alexander Nimmervoll
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Data;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Globalization;
using GenSync.Logging;
using Microsoft.Win32;

namespace CalDavSynchronizer.Ui.Reports.ViewModels
{
  public class ReportsViewModel : ModelBase, IReportViewModelParent
  {
    private readonly ObservableCollection<ReportViewModel> _reports = new ObservableCollection<ReportViewModel>();
    private readonly DelegateCommand _deleteSelectedCommand;
    private readonly DelegateCommand _saveSelectedCommand;
    private readonly ISynchronizationReportRepository _reportRepository;
    private readonly Dictionary<Guid, string> _currentProfileNamesById;
    private readonly IReportsViewModelParent _parent;
    private readonly CollectionViewSource _reportsCollectionViewSource;
    private ICollection<ReportViewModel> _selectedReports;

    public event EventHandler RequiresBringToFront;

    public virtual void RequireBringToFront ()
    {
      var handler = RequiresBringToFront;
      if (handler != null)
        handler (this, EventArgs.Empty);
    }

    public event EventHandler ReportsClosed;

    public virtual void NotifyReportsClosed ()
    {
      var handler = ReportsClosed;
      if (handler != null)
        handler (this, EventArgs.Empty);
    }

    public ReportsViewModel (
        ISynchronizationReportRepository reportRepository,
        Dictionary<Guid, string> currentProfileNamesById, IReportsViewModelParent parent)
    {
      _reportRepository = reportRepository;
      _currentProfileNamesById = currentProfileNamesById;
      _parent = parent;
      _deleteSelectedCommand = new DelegateCommand(DeleteSelected, _ => _selectedReports.Any());
      _saveSelectedCommand = new DelegateCommand(SaveSelected, _ => _selectedReports.Any());

      foreach (var reportName in reportRepository.GetAvailableReports())
        AddReportViewModel (reportName);

      // Regarding to race conditions it doesn't matter when the handler is added
      // since everything happens in the ui thread
      _reportRepository.ReportAdded += ReportRepository_ReportAdded;

      _reportsCollectionViewSource = new CollectionViewSource();
      _reportsCollectionViewSource.Source = _reports;
      _reportsCollectionViewSource.SortDescriptions.Add(new SortDescription(nameof(ReportViewModel.StartTime), ListSortDirection.Descending));
      Reports = _reportsCollectionViewSource.View;
    }


    private void ReportRepository_ReportAdded (object sender, ReportAddedEventArgs e)
    {
      AddReportViewModel (e.ReportName, e.Report);
    }

    private void AddReportViewModel (SynchronizationReportName reportName)
    {
      string profileName;
      if (!_currentProfileNamesById.TryGetValue (reportName.SyncronizationProfileId, out profileName))
        profileName = Strings.Get($"<Not existing anymore>");

      var reportProxy = new ReportProxy (reportName, () => _reportRepository.GetReport (reportName), profileName);
      var reportViewModel = new ReportViewModel (reportProxy, _reportRepository, this);
      _reports.Add (reportViewModel);
    }


    private void AddReportViewModel (SynchronizationReportName reportName, SynchronizationReport report)
    {
      string profileName;
      if (!_currentProfileNamesById.TryGetValue (reportName.SyncronizationProfileId, out profileName))
        profileName = Strings.Get($"<Not existing anymore>");

      var reportProxy = new ReportProxy (reportName, () => report, profileName);
      var reportViewModel = new ReportViewModel (reportProxy, _reportRepository, this);
      _reports.Add (reportViewModel);
    }

    private void SaveSelected (object parameter)
    {
      SaveFileDialog dialog = new SaveFileDialog();
      dialog.Filter = "Zip archives|*.zip";
      dialog.FileName = "SynchronizationReports.zip";
      dialog.Title = Strings.Get($"Save selected reports");
      if (dialog.ShowDialog() ?? false)
      {
        using (var fileStream = new FileStream (dialog.FileName, FileMode.Create))
        {
          using (var archive = new ZipArchive (fileStream, ZipArchiveMode.Create))
          {
            foreach (var report in _selectedReports)
            {
              var entry = archive.CreateEntry (report.ReportName.ToString(), CompressionLevel.Optimal);
              using (var entryStream = entry.Open())
              {
                using (var reportStream = _reportRepository.GetReportStream (report.ReportName))
                {
                  reportStream.CopyTo (entryStream);
                }
              }
            }
          }
        }
      }
    }

    private void DeleteSelected (object parameter)
    {
      foreach(var report in _selectedReports.ToArray())
      {
          report.Delete();
        _reports.Remove(report);
      }
    }

    public ICollectionView Reports { get; }
    public DelegateCommand DeleteSelectedCommand => _deleteSelectedCommand;
    public DelegateCommand SaveSelectedCommand => _saveSelectedCommand;

    public static ReportsViewModel DesignInstance
    {
      get
      {
        var designInstance = new ReportsViewModel (
            NullSynchronizationReportRepository.Instance,
            new Dictionary<Guid, string>(),
            NullReportsViewModelParent.Instance);

        designInstance._reports.Add (ReportViewModel.CreateDesignInstance());
        designInstance._reports.Add (ReportViewModel.CreateDesignInstance (true));
        designInstance._reports.Add (ReportViewModel.CreateDesignInstance (false, true));
        designInstance._reports.Add (ReportViewModel.CreateDesignInstance (true, true));

        return designInstance;
      }
    }

    public void DiplayAEntity (Guid synchronizationProfileId, string entityId)
    {
      _parent.DiplayAEntity (synchronizationProfileId, entityId);
    }

    public void DiplayBEntity (Guid synchronizationProfileId, string entityId)
    {
      ComponentContainer.EnsureSynchronizationContext ();
      _parent.DiplayBEntityAsync (synchronizationProfileId, entityId);
    }

    public void ShowLatestSynchronizationReportCommand(Guid profileId)
    {
      ReportViewModel latestReport = null;

      foreach (var report in _reports.Where(r => r.ProfileId == profileId))
        if (latestReport == null || report.StartTime > latestReport.StartTime)
          latestReport = report;

      _selectedReports.Clear();
      if (latestReport != null)
      {
        _selectedReports.Add(latestReport);
      }
    }

    public void SelectReportByName(string reportNameAsString)
    {
      _selectedReports.Clear();
      _selectedReports.Add(_reports.Single(r => r.ReportName.ToString() == reportNameAsString));
    }

    public void BindSelectedReports(ICollection<ReportViewModel> selectedReportsOrNull)
    {
      _selectedReports = selectedReportsOrNull;
    }
  }
}