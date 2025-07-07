using DynamicData;
using IOTester.Models;
using IOToolkit;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace IOTester.ViewModels
{

    public class MainWindowViewModel : ViewModelBase, IActivatableViewModel
    {
        public ViewModelActivator Activator { get; private set; } = new ViewModelActivator();
        
        public string AppRoot => AppDomain.CurrentDomain.BaseDirectory;

        private readonly SourceList<Device> _devListSource = new SourceList<Device>();
        public ReadOnlyObservableCollection<Device> Devices { get;  }

        private bool isStarted = false;
        public bool IsStared
        {
            get => isStarted;
            set
            {
                this.RaiseAndSetIfChanged(ref isStarted, value);
            }
        }

        public ReactiveCommand<Unit, Unit> OpenCloseDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewIOLogCommand { get; }
        public ReactiveCommand<Unit, Unit> EditIOConfigCommand { get; }

        private Device selectedIODevcie;
        public Device SelectedIODevcie
        {
            get => selectedIODevcie;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedIODevcie, value);
            }
        }

        public ObservableCollection<Action> tempList { get; set; } = new ObservableCollection<Action>();

        public void Load()
        {
            IORoot.Instance.SetConfig(Path.Combine(AppRoot, "Config\\IODevice.xml")).Load();
            IORoot.Instance.AfterDeserialization();

            _devListSource.Edit(_source =>
            {
                _source.Clear();

                _source.AddRange(IORoot.Instance.Devices.Where(_dev=>_dev.Type!="Standard").GroupBy(io=>io.Name).Select(g=>g.First()));
            });

            IODeviceController.Load();
        }

        public MainWindowViewModel()
        {
            OpenCloseDeviceCommand = ReactiveCommand.Create(() =>
            {
                if (IsStared)
                {
                    IODeviceController.Unload();
                }
                else
                {
                    Load();
                }
                IsStared = !IsStared;
                Devices.ToList().ForEach(_ => _.Update());
            });

            ViewIOLogCommand = ReactiveCommand.Create(() =>
            {
                var _logPath = Path.Combine(AppRoot, "Logs/IODevice.log");
                EditorLauncher.OpenWithPreferredEditor(_logPath);
            });

            EditIOConfigCommand = ReactiveCommand.Create(() =>
            {
                var _configPath = Path.Combine(AppRoot, "Config/IODevice.xml");
                EditorLauncher.OpenWithPreferredEditor(_configPath);
            });

            _devListSource
                .Connect()
                // .AutoRefresh()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out var readOnlyDevList)
                .Subscribe();
            
            Devices = readOnlyDevList;
         
            this.WhenAnyValue(_ => _.SelectedIODevcie)
                .Subscribe(_ =>
                {
                    if (_ == null) return;
                    _.TriggerUIUpdate();
                });
            
            this.WhenActivated((CompositeDisposable disposables) =>
            {

                Load();

                IsStared = true;
                Devices.ToList().ForEach(_ => _.Update());

                var _tickHandler = Observable.Interval(TimeSpan.FromMilliseconds(50))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ =>
                {
                    IODeviceController.Update();
                    if (IsStared == false) return;
                    SelectedIODevcie?.Update();
                });

                Disposable.Create(() => 
                {
                    _tickHandler.Dispose();
                    IODeviceController.Unload();
                    Debug.WriteLine("Dispose");
                }).DisposeWith(disposables);
            });
        }
    }
}
