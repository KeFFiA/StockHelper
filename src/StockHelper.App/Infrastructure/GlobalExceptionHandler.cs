using System.Windows;
using System.Windows.Threading;
using Serilog;
using StockHelper.App.Resources;
using StockHelper.App.Services;
using StockHelper.Core.Errors;

namespace StockHelper.App.Infrastructure;

/// <summary>Logs every unhandled exception and shows a clear Russian message instead of crashing.</summary>
public static class GlobalExceptionHandler
{
    private static IDialogService? _dialogs;

    public static void Register(Application app, IDialogService dialogs)
    {
        _dialogs = dialogs;
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <summary>Maps an exception to a user-facing message. Expected domain errors are not logged as errors.</summary>
    public static string GetUserMessage(Exception ex) => ex switch
    {
        DomainException domain => ErrorMessages.For(domain),
        ConcurrencyConflictException => Strings.Error_Concurrency,
        _ => Strings.Error_Unexpected,
    };

    public static void Handle(Exception ex)
    {
        var actual = Unwrap(ex);
        if (actual is DomainException or ConcurrencyConflictException)
        {
            Log.Warning(actual, "Operation rejected: {Message}", actual.Message);
        }
        else
        {
            Log.Error(actual, "Unhandled exception");
        }

        _dialogs?.ShowError(GetUserMessage(actual));
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Handle(e.Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Fatal unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
        Log.CloseAndFlush();
    }

    private static Exception Unwrap(Exception ex) =>
        ex is AggregateException { InnerExceptions.Count: 1 } aggregate ? Unwrap(aggregate.InnerExceptions[0]) : ex;
}
