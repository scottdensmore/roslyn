﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Composition;
using System.Threading;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.ExternalAccess.FSharp.Navigation;

[ExportWorkspaceService(typeof(IFSharpDocumentNavigationService)), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal class FSharpDocumentNavigationService(IThreadingContext threadingContext)
    : IFSharpDocumentNavigationService
{
    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool CanNavigateToSpan(Workspace workspace, DocumentId documentId, TextSpan textSpan)
        => CanNavigateToSpan(workspace, documentId, textSpan, CancellationToken.None);

    public bool CanNavigateToSpan(Workspace workspace, DocumentId documentId, TextSpan textSpan, CancellationToken cancellationToken)
    {
        var service = workspace.Services.GetService<IDocumentNavigationService>();
        return threadingContext.JoinableTaskFactory.Run(() =>
            service.CanNavigateToSpanAsync(workspace, documentId, textSpan, cancellationToken));
    }

    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool CanNavigateToLineAndOffset(Workspace workspace, DocumentId documentId, int lineNumber, int offset)
        => CanNavigateToLineAndOffset(workspace, documentId, lineNumber, offset, CancellationToken.None);

    [Obsolete("Call overloads that take a span or position", error: false)]
    public bool CanNavigateToLineAndOffset(Workspace workspace, DocumentId documentId, int lineNumber, int offset, CancellationToken cancellationToken)
        => false;

    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool CanNavigateToPosition(Workspace workspace, DocumentId documentId, int position, int virtualSpace)
        => CanNavigateToPosition(workspace, documentId, position, virtualSpace, CancellationToken.None);

    public bool CanNavigateToPosition(Workspace workspace, DocumentId documentId, int position, int virtualSpace, CancellationToken cancellationToken)
    {
        var service = workspace.Services.GetService<IDocumentNavigationService>();
        return threadingContext.JoinableTaskFactory.Run(() =>
            service.CanNavigateToPositionAsync(workspace, documentId, position, virtualSpace, cancellationToken));
    }

    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool TryNavigateToSpan(Workspace workspace, DocumentId documentId, TextSpan textSpan, OptionSet options)
        => TryNavigateToSpan(workspace, documentId, textSpan, CancellationToken.None);

    public bool TryNavigateToSpan(Workspace workspace, DocumentId documentId, TextSpan textSpan, CancellationToken cancellationToken)
    {
        var service = workspace.Services.GetService<IDocumentNavigationService>();
        return threadingContext.JoinableTaskFactory.Run(() =>
            service.TryNavigateToSpanAsync(
                threadingContext, workspace, documentId, textSpan, NavigationOptions.Default with { PreferProvisionalTab = true }, cancellationToken));
    }

    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool TryNavigateToLineAndOffset(Workspace workspace, DocumentId documentId, int lineNumber, int offset, OptionSet options)
        => TryNavigateToLineAndOffset(workspace, documentId, lineNumber, offset, CancellationToken.None);

    public bool TryNavigateToLineAndOffset(Workspace workspace, DocumentId documentId, int lineNumber, int offset, CancellationToken cancellationToken)
    {
        var service = workspace.Services.GetService<IDocumentNavigationService>();
        return threadingContext.JoinableTaskFactory.Run(() =>
            service.TryNavigateToPositionAsync(
                threadingContext, workspace, documentId, lineNumber, offset, NavigationOptions.Default with { PreferProvisionalTab = true }, cancellationToken));
    }

    [Obsolete("Call overload that takes a CancellationToken", error: false)]
    public bool TryNavigateToPosition(Workspace workspace, DocumentId documentId, int position, int virtualSpace, OptionSet options)
        => TryNavigateToPosition(workspace, documentId, position, virtualSpace, CancellationToken.None);

    public bool TryNavigateToPosition(Workspace workspace, DocumentId documentId, int position, int virtualSpace, CancellationToken cancellationToken)
    {
        var service = workspace.Services.GetService<IDocumentNavigationService>();
        return threadingContext.JoinableTaskFactory.Run(() =>
            service.TryNavigateToPositionAsync(
                threadingContext, workspace, documentId, position, virtualSpace, NavigationOptions.Default with { PreferProvisionalTab = true }, cancellationToken));
    }
}
