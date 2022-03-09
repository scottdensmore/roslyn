﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.BackgroundWorkIndicator;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.GoToDefinition
{
    [Export(typeof(ICommandHandler))]
    [ContentType(ContentTypeNames.RoslynContentType)]
    [Name(PredefinedCommandHandlerNames.GoToDefinition)]
    internal class GoToDefinitionCommandHandler :
        ICommandHandler<GoToDefinitionCommandArgs>
    {
        private readonly IGlobalOptionService _globalOptionService;
        private readonly IThreadingContext _threadingContext;
        private readonly IUIThreadOperationExecutor _executor;
        private readonly IAsynchronousOperationListener _listener;

        [ImportingConstructor]
        [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification = "Used in test code: https://github.com/dotnet/roslyn/issues/42814")]
        public GoToDefinitionCommandHandler(
            IGlobalOptionService globalOptionService,
            IThreadingContext threadingContext,
            IUIThreadOperationExecutor executor,
            IAsynchronousOperationListenerProvider listenerProvider)
        {
            _globalOptionService = globalOptionService;
            _threadingContext = threadingContext;
            _executor = executor;
            _listener = listenerProvider.GetListener(FeatureAttribute.GoToDefinition);
        }

        public string DisplayName => EditorFeaturesResources.Go_to_Definition;

        private static (Document?, IGoToDefinitionService?, IAsyncGoToDefinitionService?) GetDocumentAndService(ITextSnapshot snapshot)
        {
            var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
            return (document, document?.GetLanguageService<IGoToDefinitionService>(), document?.GetLanguageService<IAsyncGoToDefinitionService>());
        }

        public CommandState GetCommandState(GoToDefinitionCommandArgs args)
        {
            var (_, service, asyncService) = GetDocumentAndService(args.SubjectBuffer.CurrentSnapshot);
            return service != null || asyncService != null
                ? CommandState.Available
                : CommandState.Unspecified;
        }

        public bool ExecuteCommand(GoToDefinitionCommandArgs args, CommandExecutionContext context)
        {
            var subjectBuffer = args.SubjectBuffer;
            var (document, service, asyncService) = GetDocumentAndService(subjectBuffer.CurrentSnapshot);

            if (service == null && asyncService == null)
                return false;

            // In Live Share, typescript exports a gotodefinition service that returns no results and prevents the LSP client
            // from handling the request.  So prevent the local service from handling goto def commands in the remote workspace.
            // This can be removed once typescript implements LSP support for goto def.
            if (subjectBuffer.IsInLspEditorContext())
                return false;

            Contract.ThrowIfNull(document);
            var caretPos = args.TextView.GetCaretPoint(subjectBuffer);
            if (!caretPos.HasValue)
                return false;

            if (asyncService != null && _globalOptionService.GetOption(FeatureOnOffOptions.NavigateAsynchronously))
            {
                // We're showing our own UI, ensure the editor doesn't show anything itself.
                context.OperationContext.TakeOwnership();
                var token = _listener.BeginAsyncOperation(nameof(ExecuteCommand));
                ExecuteAsynchronouslyAsync(args, document, asyncService, caretPos.Value)
                    .ReportNonFatalErrorAsync()
                    .CompletesAsyncOperation(token);
            }
            else
            {
                // The language either doesn't support async goto-def, or the option is disabled to navigate
                // asynchronously.  So fall back to normal synchronous navigation.
                var succeeded = ExecuteSynchronously(document, service, asyncService, caretPos.Value, context);

                if (!succeeded)
                {
                    // Dismiss any context dialog that is up before showing our own notification.
                    context.OperationContext.TakeOwnership();
                    ReportFailure(document);
                }
            }

            return true;
        }

        private bool ExecuteSynchronously(
            Document document,
            IGoToDefinitionService? service,
            IAsyncGoToDefinitionService? asyncService,
            int position,
            CommandExecutionContext context)
        {
            using (context.OperationContext.AddScope(allowCancellation: true, EditorFeaturesResources.Navigating_to_definition))
            {
                var cancellationToken = context.OperationContext.UserCancellationToken;
                if (asyncService != null)
                {
                    return _threadingContext.JoinableTaskFactory.Run(async () =>
                    {
                        // determine the location first.
                        var location = await asyncService.FindDefinitionLocationAsync(
                            document, position, cancellationToken).ConfigureAwait(false);
                        return await location.TryNavigateToAsync(
                            _threadingContext, NavigationOptions.Default, cancellationToken).ConfigureAwait(false);
                    });
                }
                else if (service != null)
                {
                    return service.TryGoToDefinition(document, position, cancellationToken);
                }
                else
                {
                    throw ExceptionUtilities.Unreachable;
                }
            }
        }

        private static void ReportFailure(Document document)
        {
            var notificationService = document.Project.Solution.Workspace.Services.GetRequiredService<INotificationService>();
            notificationService.SendNotification(
                FeaturesResources.Cannot_navigate_to_the_symbol_under_the_caret, EditorFeaturesResources.Go_to_Definition, NotificationSeverity.Information);
        }

        private async Task ExecuteAsynchronouslyAsync(
            GoToDefinitionCommandArgs args, Document document, IAsyncGoToDefinitionService service, SnapshotPoint position)
        {
            bool succeeded;

            var indicatorFactory = document.Project.Solution.Workspace.Services.GetRequiredService<IBackgroundWorkIndicatorFactory>();
            using (var backgroundIndicator = indicatorFactory.Create(
                args.TextView, new SnapshotSpan(args.SubjectBuffer.CurrentSnapshot, position, 1),
                EditorFeaturesResources.Navigating_to_definition))
            {
                var cancellationToken = backgroundIndicator.UserCancellationToken;

                // determine the location first.
                var location = await service.FindDefinitionLocationAsync(document, position, cancellationToken).ConfigureAwait(false);

                // make sure that if our background indicator got canceled, that we do not still perform the navigation.
                if (backgroundIndicator.UserCancellationToken.IsCancellationRequested)
                    return;

                // we're about to navigate.  so disable cancellation on focus-lost in our indicator so we don't end up
                // causing ourselves to self-cancel.
                backgroundIndicator.CancelOnFocusLost = false;
                succeeded = await location.TryNavigateToAsync(
                    _threadingContext, NavigationOptions.Default, cancellationToken).ConfigureAwait(false);
            }

            if (!succeeded)
            {
                await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);
                ReportFailure(document);
            }
        }
    }
}
