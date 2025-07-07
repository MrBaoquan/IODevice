using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTester.ViewModels
{
    public class TestViewModel :ViewModelBase, IActivatableViewModel
    {
        public List<Action> Actions { get; set; } = new List<Action>();

        private readonly SourceList<Action> _actionsSource = new SourceList<Action>();
        public ReadOnlyObservableCollection<Action> ActionList { get; }

        public ViewModelActivator Activator { get; private set; } = new ViewModelActivator();

        public void Init(string Key)
        {
            Actions = Enumerable.Range(0, 5).Select(_ => new Action
            {
                Name = Key,
                Keys = new List<Key>
                {
                   new Key{Name=$"{Key}_Button " + _}
                }
            }).ToList();
            _actionsSource.Edit(inner =>
            {
                inner.Clear();
                inner.AddRange(Actions);
            });
        }

        public void Update(string Key)
        {
            Actions.ForEach(_action =>
            {
                _action.Name = Key;
            });
        }

        public  TestViewModel()
        {
            _actionsSource
                .Connect()
                .AutoRefresh()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out var readOnlyList)
                .Subscribe();
            ActionList = readOnlyList;
        }
    }
}
