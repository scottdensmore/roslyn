﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.VisualStudio.LanguageServices.DocumentOutline
{
    /// <summary>
    /// Manages access to Visual Studio code window information in a thread-safe way.
    /// </summary>
    internal sealed class VisualStudioCodeWindowInfoService
    {
        private readonly IVsCodeWindow _codeWindow;
        private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
        private readonly IThreadingContext _threadingContext;

        public VisualStudioCodeWindowInfoService(
            IVsCodeWindow codeWindow,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            IThreadingContext threadingContext)
        {
            threadingContext.ThrowIfNotOnUIThread();
            _codeWindow = codeWindow;
            _editorAdaptersFactoryService = editorAdaptersFactoryService;
            _threadingContext = threadingContext;
        }

        /// <summary>
        /// Get <see cref="DocumentSymbolRequestInfo"/>, switching to the UI thread if necessary.
        /// </summary>
        public async Task<DocumentSymbolRequestInfo?> GetDocumentSymbolRequestInfoAsync(CancellationToken token)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            return GetDocumentSymbolRequestInfo();
        }

        private DocumentSymbolRequestInfo? GetDocumentSymbolRequestInfo()
        {
            _threadingContext.ThrowIfNotOnUIThread();
            var wpfTextView = GetLastActiveIWpfTextView();
            var textBuffer = wpfTextView.TextBuffer;
            if (_editorAdaptersFactoryService.GetBufferAdapter(textBuffer) is IPersistFileFormat persistFileFormat &&
                ErrorHandler.Succeeded(persistFileFormat.GetCurFile(out var filePath, out var _)))
            {
                return new DocumentSymbolRequestInfo(textBuffer, filePath);
            }

            // Rosyln language services have been registered for a buffer that does not exist on disk and LSP requires a file path.
            return null;
        }

        /// <summary>
        /// Returns a version of this service whose apis can only be called on the UI thread.
        /// </summary>
        public IVisualStudioCodeWindowInfoServiceUIThreadOperations GetServiceAndThrowIfNotOnUIThread()
        {
            _threadingContext.ThrowIfNotOnUIThread();
            return new VisualStudioCodeWindowInfoService_OnlyCallOnUIThread(this);
        }

        private SnapshotPoint? GetCurrentCaretSnapshotPoint()
        {
            _threadingContext.ThrowIfNotOnUIThread();
            var activeTextView = GetLastActiveIWpfTextView();
            if (activeTextView is null)
            {
                return null;
            }

            var textBuffer = activeTextView.TextBuffer;
            return activeTextView.GetCaretPoint(textBuffer);
        }

        public async Task<SnapshotPoint?> GetSnapshotPointFromCaretPositionAsync(CaretPosition newPosition, CancellationToken token)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            return GetSnapshotPointFromCaretPosition(newPosition);
        }

        private SnapshotPoint? GetSnapshotPointFromCaretPosition(CaretPosition newPosition)
        {
            _threadingContext.ThrowIfNotOnUIThread();
            var activeTextView = GetLastActiveIWpfTextView();
            Assumes.NotNull(activeTextView);

            var textBuffer = activeTextView.TextBuffer;
            return newPosition.Point.GetPoint(textBuffer, PositionAffinity.Predecessor);
        }

        public async Task<IWpfTextView> GetLastActiveIWpfTextViewAsync(CancellationToken token)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            return GetLastActiveIWpfTextView();
        }

        /// <summary>
        /// Get the last active text view for our code window.
        /// Is guaranteed to be a part of our code window and therefore applicable to our language service.
        /// Will return either the primary or secondary text view from the code window and nothing else.
        /// Only returns null if this method is called before content has been established for the adapter,
        /// </summary>
        private IWpfTextView GetLastActiveIWpfTextView()
        {
            _threadingContext.ThrowIfNotOnUIThread();

            if (ErrorHandler.Failed(_codeWindow.GetLastActiveView(out var textView)))
            {
                FailFast.Fail("Unable to get the last active text view. IVsCodeWindow implementation we are given is invalid.");
            }

            var wpfTextView = _editorAdaptersFactoryService.GetWpfTextView(textView);
            Assumes.NotNull(wpfTextView);
            return wpfTextView;
        }

        /// <summary>
        /// The apis is this class can only be called from the UI thread.
        /// </summary>
        private sealed class VisualStudioCodeWindowInfoService_OnlyCallOnUIThread : IVisualStudioCodeWindowInfoServiceUIThreadOperations
        {
            private readonly VisualStudioCodeWindowInfoService _service;

            public VisualStudioCodeWindowInfoService_OnlyCallOnUIThread(VisualStudioCodeWindowInfoService service)
                => _service = service;

            public DocumentSymbolRequestInfo? GetDocumentSymbolRequestInfo()
                => _service.GetDocumentSymbolRequestInfo();

            public SnapshotPoint? GetCurrentCaretSnapshotPoint()
                => _service.GetCurrentCaretSnapshotPoint();

            public IWpfTextView GetLastActiveIWpfTextView()
                => _service.GetLastActiveIWpfTextView();
        }
    }
}
