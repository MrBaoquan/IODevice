---
description: '.NET WPF/Avalonia component and application patterns'
applyTo: '**/*.axaml, **/*.xaml, **/*.cs'
---

## Summary

These instructions guide GitHub Copilot to assist with building high-quality, maintainable, and performant **Avalonia / WPF** desktop applications using the MVVM pattern. It includes best practices for XAML/AXAML, data binding, UI responsiveness, and .NET performance.

> **Project Context:** This project uses **Avalonia 11.x** (not WPF). Prefer Avalonia-specific APIs, ReactiveUI, and `*.axaml` file conventions over WPF-specific APIs.

## Ideal Project Types

- Desktop applications using C# and Avalonia UI / WPF
- Applications following the MVVM (Model-View-ViewModel) design pattern
- Projects using .NET 6.0 or later
- UI components built in AXAML/XAML
- Solutions emphasizing performance and responsiveness

## Goals

- Generate boilerplate for `INotifyPropertyChanged`, `ReactiveObject`, and `ICommand` / `ReactiveCommand`
- Suggest clean separation of ViewModel and View logic
- Encourage use of `ObservableCollection<T>`, `ICommand`, `ReactiveCommand`, and proper binding
- Recommend performance tips (e.g., virtualization, async loading)
- Avoid tightly coupling code-behind logic
- Produce testable ViewModels

## Avalonia/ReactiveUI Specific Patterns

```csharp
// ViewModel inherits ReactiveObject (Avalonia + ReactiveUI pattern)
public class MainViewModel : ViewModelBase  // ViewModelBase : ReactiveObject
{
    private string _userName = string.Empty;

    public string UserName
    {
        get => _userName;
        set => this.RaiseAndSetIfChanged(ref _userName, value);
    }

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }

    public MainViewModel()
    {
        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
    }

    private async Task LoginAsync()
    {
        // login logic
    }
}
```

```xml
<!-- Avalonia AXAML binding with compiled bindings -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <StackPanel>
        <TextBox Text="{Binding UserName}" />
        <Button Content="Login" Command="{Binding LoginCommand}" />
    </StackPanel>
</UserControl>
```

## Common Patterns to Follow

- ViewModel-first binding with compiled bindings (`x:DataType`)
- Dependency Injection using Microsoft.Extensions.DependencyInjection or Splat
- AXAML naming conventions (PascalCase for controls, camelCase for bindings)
- Avoiding magic strings in binding (use `nameof`)
- `WhenActivated` for managing subscriptions in `IActivatableViewModel`
- `ReactiveCommand.CreateFromTask` for async operations
- `ReactiveCommand.Create` for sync operations

## Suggestions (Good)

- "Generate a ViewModel for a screen with ReactiveCommand and RaiseAndSetIfChanged"
- "Write an AXAML snippet for a ListBox that uses UI virtualization and binds to an ObservableCollection"
- "Refactor this code-behind click handler into a ReactiveCommand in the ViewModel"
- "Add a loading spinner while fetching data asynchronously in Avalonia"

## Avoid

- Suggesting business logic in code-behind (`.axaml.cs`)
- Using static event handlers without context
- Generating tightly coupled AXAML without binding
- Suggesting WinForms or UWP approaches
- Using WPF-only APIs (`System.Windows.*`) — use Avalonia equivalents instead

## Technologies to Prefer

- C# with .NET 6.0+
- AXAML with MVVM structure using ReactiveUI
- `ReactiveCommand<TInput, TOutput>` for commands
- Async/await for non-blocking UI
- `ObservableCollection<T>`, `ReactiveCommand`, `ReactiveObject`, `IActivatableViewModel`
- SkiaSharp for custom rendering (via `DrawingContext` in Avalonia)
