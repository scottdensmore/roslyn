﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed partial class LocalRewriter
    {
        private BoundExpression MakeDynamicIndexerAccessReceiver(BoundDynamicIndexerAccess indexerAccess, BoundExpression loweredReceiver)
        {
            BoundExpression result;

            string? indexedPropertyName = indexerAccess.TryGetIndexedPropertyName();
            if (indexedPropertyName != null)
            {
                // Dev12 forces the receiver to be typed to dynamic to workaround a bug in the runtime binder.
                // See DynamicRewriter::FixupIndexedProperty:
                // "If we don't do this, then the calling object is statically typed and we pass the UseCompileTimeType to the runtime binder."
                // However, with the cast the scenarios don't work either, so we don't mimic Dev12.
                // loweredReceiver = BoundConversion.Synthesized(loweredReceiver.Syntax, loweredReceiver, Conversion.Identity, false, false, null, DynamicTypeSymbol.Instance);

                result = _dynamicFactory.MakeDynamicGetMember(loweredReceiver, indexedPropertyName, resultIndexed: true).ToExpression();
            }
            else
            {
                result = loweredReceiver;
            }

            return result;
        }

        public override BoundNode VisitDynamicIndexerAccess(BoundDynamicIndexerAccess node)
        {
            var loweredReceiver = VisitExpression(node.Receiver);
            // There are no target types for dynamic expression.
            AssertNoImplicitInterpolatedStringHandlerConversions(node.Arguments);
            var loweredArguments = VisitList(node.Arguments);

            return MakeDynamicGetIndex(node, loweredReceiver, loweredArguments, node.ArgumentNamesOpt, node.ArgumentRefKindsOpt);
        }

        private BoundExpression MakeDynamicGetIndex(
            BoundDynamicIndexerAccess node,
            BoundExpression loweredReceiver,
            ImmutableArray<BoundExpression> loweredArguments,
            ImmutableArray<string> argumentNames,
            ImmutableArray<RefKind> refKinds)
        {
            // If we are calling a method on a NoPIA type, we need to embed all methods/properties
            // with the matching name of this dynamic invocation.
            EmbedIfNeedTo(loweredReceiver, node.ApplicableIndexers, node.Syntax);

            return _dynamicFactory.MakeDynamicGetIndex(
                MakeDynamicIndexerAccessReceiver(node, loweredReceiver),
                loweredArguments,
                argumentNames,
                refKinds).ToExpression();
        }

        public override BoundNode VisitIndexerAccess(BoundIndexerAccess node)
        {
            Debug.Assert(node.Indexer.IsIndexer || node.Indexer.IsIndexedProperty);
            Debug.Assert((object?)node.Indexer.GetOwnOrInheritedGetMethod() != null);

            return VisitIndexerAccess(node, isLeftOfAssignment: false);
        }

        private BoundExpression VisitIndexerAccess(BoundIndexerAccess node, bool isLeftOfAssignment)
        {
            PropertySymbol indexer = node.Indexer;
            Debug.Assert(indexer.IsIndexer || indexer.IsIndexedProperty);

            // Rewrite the receiver.
            BoundExpression? rewrittenReceiver = VisitExpression(node.ReceiverOpt);
            Debug.Assert(rewrittenReceiver is { });

            return MakeIndexerAccess(
                node.Syntax,
                rewrittenReceiver,
                indexer,
                node.Arguments,
                node.ArgumentNamesOpt,
                node.ArgumentRefKindsOpt,
                node.Expanded,
                node.ArgsToParamsOpt,
                node.DefaultArguments,
                node.Type,
                node,
                isLeftOfAssignment);
        }

        private BoundExpression MakeIndexerAccess(
            SyntaxNode syntax,
            BoundExpression rewrittenReceiver,
            PropertySymbol indexer,
            ImmutableArray<BoundExpression> arguments,
            ImmutableArray<string> argumentNamesOpt,
            ImmutableArray<RefKind> argumentRefKindsOpt,
            bool expanded,
            ImmutableArray<int> argsToParamsOpt,
            BitVector defaultArguments,
            TypeSymbol type,
            BoundIndexerAccess? oldNodeOpt,
            bool isLeftOfAssignment)
        {
            if (isLeftOfAssignment && indexer.RefKind == RefKind.None)
            {
                // This is an indexer set access. We return a BoundIndexerAccess node here.
                // This node will be rewritten with MakePropertyAssignment when rewriting the enclosing BoundAssignmentOperator.

                return oldNodeOpt != null ?
                    oldNodeOpt.Update(rewrittenReceiver, indexer, arguments, argumentNamesOpt, argumentRefKindsOpt, expanded, argsToParamsOpt, defaultArguments, type) :
                    new BoundIndexerAccess(syntax, rewrittenReceiver, indexer, arguments, argumentNamesOpt, argumentRefKindsOpt, expanded, argsToParamsOpt, defaultArguments, type);
            }
            else
            {
                var getMethod = indexer.GetOwnOrInheritedGetMethod();
                Debug.Assert(getMethod is not null);

                ImmutableArray<BoundExpression> rewrittenArguments = VisitArguments(
                    arguments,
                    indexer,
                    argsToParamsOpt,
                    argumentRefKindsOpt,
                    ref rewrittenReceiver!,
                    out ArrayBuilder<LocalSymbol>? temps);

                rewrittenArguments = MakeArguments(
                    syntax,
                    rewrittenArguments,
                    indexer,
                    expanded,
                    argsToParamsOpt,
                    ref argumentRefKindsOpt,
                    ref temps,
                    enableCallerInfo: ThreeState.True);

                BoundExpression call = MakePropertyGetAccess(syntax, rewrittenReceiver, indexer, rewrittenArguments, getMethod);

                if (temps.Count == 0)
                {
                    temps.Free();
                    return call;
                }
                else
                {
                    return new BoundSequence(
                        syntax,
                        temps.ToImmutableAndFree(),
                        ImmutableArray<BoundExpression>.Empty,
                        call,
                        type);
                }
            }
        }

        public override BoundNode VisitIndexOrRangePatternIndexerAccess(BoundIndexOrRangePatternIndexerAccess node)
        {
            return VisitIndexOrRangePatternIndexerAccess(node, isLeftOfAssignment: false);
        }

        private BoundSequence VisitIndexOrRangePatternIndexerAccess(BoundIndexOrRangePatternIndexerAccess node, bool isLeftOfAssignment)
        {
            if (TypeSymbol.Equals(
                node.Argument.Type,
                _compilation.GetWellKnownType(WellKnownType.System_Index),
                TypeCompareKind.ConsiderEverything))
            {
                return VisitIndexPatternIndexerAccess(
                    node.Syntax,
                    node.Receiver,
                    node.LengthOrCountProperty,
                    (PropertySymbol)node.PatternSymbol,
                    node.Argument,
                    isLeftOfAssignment: isLeftOfAssignment);
            }
            else
            {
                Debug.Assert(TypeSymbol.Equals(
                    node.Argument.Type,
                    _compilation.GetWellKnownType(WellKnownType.System_Range),
                    TypeCompareKind.ConsiderEverything));
                return VisitRangePatternIndexerAccess(
                    node.Receiver,
                    node.LengthOrCountProperty,
                    (MethodSymbol)node.PatternSymbol,
                    node.Argument);
            }
        }


        private BoundSequence VisitIndexPatternIndexerAccess(
            SyntaxNode syntax,
            BoundExpression receiver,
            PropertySymbol lengthOrCountProperty,
            PropertySymbol intIndexer,
            BoundExpression argument,
            bool isLeftOfAssignment)
        {
            var F = _factory;

            var locals = ArrayBuilder<LocalSymbol>.GetInstance(2);
            var sideeffects = ArrayBuilder<BoundExpression>.GetInstance(2);

            Debug.Assert(receiver.Type is { });
            var receiverLocal = F.StoreToTemp(
                VisitExpression(receiver),
                out var receiverStore,
                // Store the receiver as a ref local if it's a value type to ensure side effects are propagated
                receiver.Type.IsReferenceType ? RefKind.None : RefKind.Ref);
            locals.Add(receiverLocal.LocalSymbol);
            sideeffects.Add(receiverStore);

            BoundExpression makeOffsetInput = DetermineMakePatternIndexOffsetExpressionStrategy(argument, out PatternIndexOffsetLoweringStrategy strategy);
            BoundExpression indexAccess;

            switch (strategy)
            {
                case PatternIndexOffsetLoweringStrategy.SubtractFromLength:
                    // ensure we evaluate the input before accessing length
                    if (makeOffsetInput.ConstantValue is null)
                    {
                        makeOffsetInput = F.StoreToTemp(makeOffsetInput, out BoundAssignmentOperator inputStore);
                        locals.Add(((BoundLocal)makeOffsetInput).LocalSymbol);
                        sideeffects.Add(inputStore);
                    }

                    indexAccess = MakePatternIndexOffsetExpression(makeOffsetInput, F.Property(receiverLocal, lengthOrCountProperty), strategy);
                    break;

                case PatternIndexOffsetLoweringStrategy.UseAsIs:
                    indexAccess = MakePatternIndexOffsetExpression(makeOffsetInput, lengthAccess: null, strategy);
                    break;

                case PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI:
                    indexAccess = MakePatternIndexOffsetExpression(makeOffsetInput, F.Property(receiverLocal, lengthOrCountProperty), strategy);
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(strategy);
            }

            return (BoundSequence)F.Sequence(
                locals.ToImmutableAndFree(),
                sideeffects.ToImmutableAndFree(),
                MakeIndexerAccess(
                    syntax,
                    receiverLocal,
                    intIndexer,
                    ImmutableArray.Create<BoundExpression>(indexAccess),
                    default,
                    default,
                    expanded: false,
                    argsToParamsOpt: default,
                    defaultArguments: default,
                    intIndexer.Type,
                    oldNodeOpt: null,
                    isLeftOfAssignment));
        }

        /// <summary>
        /// Used to produce an expression translating <paramref name="loweredExpr"/> to an integer offset
        /// according to the <paramref name="strategy"/>.
        /// The implementation should be in sync with <see cref="DetermineMakePatternIndexOffsetExpressionStrategy"/>.
        /// </summary>
        /// <param name="loweredExpr">The lowered input for the translation</param>
        /// <param name="lengthAccess">
        /// An expression accessing the length of the indexing target. This should
        /// be a non-side-effecting operation.
        /// </param>
        /// <param name="strategy">The translation strategy</param>
        private BoundExpression MakePatternIndexOffsetExpression(
            BoundExpression? loweredExpr,
            BoundExpression? lengthAccess,
            PatternIndexOffsetLoweringStrategy strategy)
        {
            switch (strategy)
            {
                case PatternIndexOffsetLoweringStrategy.Zero:
                    return _factory.Literal(0);

                case PatternIndexOffsetLoweringStrategy.Length:
                    Debug.Assert(lengthAccess is not null);
                    return lengthAccess;

                case PatternIndexOffsetLoweringStrategy.SubtractFromLength:
                    Debug.Assert(loweredExpr is not null);
                    Debug.Assert(lengthAccess is not null);
                    Debug.Assert(loweredExpr.Type!.SpecialType == SpecialType.System_Int32);

                    if (loweredExpr.ConstantValue?.Int32Value == 0)
                    {
                        return lengthAccess;
                    }

                    return _factory.IntSubtract(lengthAccess, loweredExpr);

                case PatternIndexOffsetLoweringStrategy.UseAsIs:
                    Debug.Assert(loweredExpr is not null);
                    Debug.Assert(loweredExpr.Type!.SpecialType == SpecialType.System_Int32);
                    return loweredExpr;

                case PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI:
                    Debug.Assert(loweredExpr is not null);
                    Debug.Assert(lengthAccess is not null);
                    Debug.Assert(TypeSymbol.Equals(
                        loweredExpr.Type,
                        _compilation.GetWellKnownType(WellKnownType.System_Index),
                        TypeCompareKind.ConsiderEverything));

                    return _factory.Call(
                        loweredExpr,
                        WellKnownMember.System_Index__GetOffset,
                        lengthAccess);

                default:
                    throw ExceptionUtilities.UnexpectedValue(strategy);
            }
        }

        private enum PatternIndexOffsetLoweringStrategy
        {
            Zero,
            Length,
            SubtractFromLength,
            UseAsIs,
            UseGetOffsetAPI
        }

        /// <summary>
        /// Determine the lowering strategy for translating a System.Index value to an integer offset value
        /// and prepare the lowered input for the translation process handled by <see cref="MakePatternIndexOffsetExpression"/>.
        /// The implementation should be in sync with <see cref="MakePatternIndexOffsetExpression"/>.
        /// </summary>
        private BoundExpression DetermineMakePatternIndexOffsetExpressionStrategy(
            BoundExpression unloweredExpr,
            out PatternIndexOffsetLoweringStrategy strategy)
        {
            Debug.Assert(TypeSymbol.Equals(
                unloweredExpr.Type,
                _compilation.GetWellKnownType(WellKnownType.System_Index),
                TypeCompareKind.ConsiderEverything));

            if (unloweredExpr is BoundFromEndIndexExpression hatExpression)
            {
                // If the System.Index argument is `^index`, we can replace the
                // `argument.GetOffset(length)` call with `length - index`
                Debug.Assert(hatExpression.Operand is { Type: { SpecialType: SpecialType.System_Int32 } });
                strategy = PatternIndexOffsetLoweringStrategy.SubtractFromLength;
                return VisitExpression(hatExpression.Operand);
            }
            else if (unloweredExpr is BoundConversion { Operand: { Type: { SpecialType: SpecialType.System_Int32 } } operand })
            {
                // If the System.Index argument is a conversion from int to Index we
                // can return the int directly
                strategy = PatternIndexOffsetLoweringStrategy.UseAsIs;
                return VisitExpression(operand);
            }
            else
            {
                // `argument.GetOffset(length)`
                strategy = PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI;
                return VisitExpression(unloweredExpr);
            }
        }

        private BoundSequence VisitRangePatternIndexerAccess(
            BoundExpression receiver,
            PropertySymbol lengthOrCountProperty,
            MethodSymbol sliceMethod,
            BoundExpression rangeArg)
        {
            Debug.Assert(TypeSymbol.Equals(
                rangeArg.Type,
                _compilation.GetWellKnownType(WellKnownType.System_Range),
                TypeCompareKind.ConsiderEverything));

            // Lowered code without optimizations:
            // var receiver = receiverExpr;
            // Range range = argumentExpr;
            // int length = receiver.length;
            // int start = range.Start.GetOffset(length)
            // int rangeSize = range.End.GetOffset(length) - start
            // receiver.Slice(start, rangeSize)

            var F = _factory;

            var localsBuilder = ArrayBuilder<LocalSymbol>.GetInstance();
            var sideEffectsBuilder = ArrayBuilder<BoundExpression>.GetInstance();

            var receiverLocal = F.StoreToTemp(VisitExpression(receiver), out var receiverStore);

            localsBuilder.Add(receiverLocal.LocalSymbol);
            sideEffectsBuilder.Add(receiverStore);

            BoundExpression startExpr;
            BoundExpression rangeSizeExpr;
            if (rangeArg is BoundRangeExpression rangeExpr)
            {
                // If we know that the input is a range expression, we can
                // optimize by pulling it apart inline, so
                // 
                // Range range = argumentExpr;
                // int start = range.Start.GetOffset(length)
                // int rangeSize = range.End.GetOffset(length) - start
                //
                // is, with `start..end`:
                //
                // int start = start.GetOffset(length)
                // int rangeSize = end.GetOffset(length) - start

                BoundExpression? startMakeOffsetInput;
                PatternIndexOffsetLoweringStrategy startStrategy;

                if (rangeExpr.LeftOperandOpt is BoundExpression left)
                {
                    startMakeOffsetInput = DetermineMakePatternIndexOffsetExpressionStrategy(left, out startStrategy);
                }
                else
                {
                    startStrategy = PatternIndexOffsetLoweringStrategy.Zero;
                    startMakeOffsetInput = null;
                }

                BoundExpression? endMakeOffsetInput;
                PatternIndexOffsetLoweringStrategy endStrategy;

                if (rangeExpr.RightOperandOpt is BoundExpression right)
                {
                    endMakeOffsetInput = DetermineMakePatternIndexOffsetExpressionStrategy(right, out endStrategy);
                }
                else
                {
                    endStrategy = PatternIndexOffsetLoweringStrategy.Length;
                    endMakeOffsetInput = null;
                }

                const int captureStartOffset = 1 << 0;
                const int captureEndOffset = 1 << 1;
                const int useLength = 1 << 2;
                const int captureLength = 1 << 3;
                const int captureStartValue = 1 << 4;

                int rewriteFlags;

                switch ((startStrategy, endStrategy))
                {
                    case (PatternIndexOffsetLoweringStrategy.Zero, PatternIndexOffsetLoweringStrategy.Length):
                    case (PatternIndexOffsetLoweringStrategy.Zero, PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI):
                        rewriteFlags = useLength;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.Zero, PatternIndexOffsetLoweringStrategy.SubtractFromLength):
                        rewriteFlags = captureEndOffset | useLength;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.Zero, PatternIndexOffsetLoweringStrategy.UseAsIs):
                        rewriteFlags = 0;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.UseAsIs, PatternIndexOffsetLoweringStrategy.Length):
                    case (PatternIndexOffsetLoweringStrategy.UseAsIs, PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI):
                        rewriteFlags = useLength | captureStartValue;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.UseAsIs, PatternIndexOffsetLoweringStrategy.SubtractFromLength):
                        rewriteFlags = captureStartOffset | captureEndOffset | useLength;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.UseAsIs, PatternIndexOffsetLoweringStrategy.UseAsIs):
                        rewriteFlags = captureStartValue;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.SubtractFromLength, PatternIndexOffsetLoweringStrategy.Length):
                    case (PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI, PatternIndexOffsetLoweringStrategy.Length):
                        rewriteFlags = captureStartOffset | useLength | captureLength | captureStartValue;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.SubtractFromLength, PatternIndexOffsetLoweringStrategy.SubtractFromLength):
                    case (PatternIndexOffsetLoweringStrategy.SubtractFromLength, PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI):
                    case (PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI, PatternIndexOffsetLoweringStrategy.SubtractFromLength):
                    case (PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI, PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI):
                        rewriteFlags = captureStartOffset | captureEndOffset | useLength | captureLength | captureStartValue;
                        break;
                    case (PatternIndexOffsetLoweringStrategy.SubtractFromLength, PatternIndexOffsetLoweringStrategy.UseAsIs):
                    case (PatternIndexOffsetLoweringStrategy.UseGetOffsetAPI, PatternIndexOffsetLoweringStrategy.UseAsIs):
                        rewriteFlags = captureStartOffset | captureEndOffset | useLength | captureStartValue;
                        break;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(startStrategy);
                }

                Debug.Assert(startStrategy != PatternIndexOffsetLoweringStrategy.Zero || (rewriteFlags & captureStartOffset) == 0);
                Debug.Assert(startStrategy != PatternIndexOffsetLoweringStrategy.Zero || (rewriteFlags & captureStartValue) == 0);
                Debug.Assert((rewriteFlags & captureEndOffset) == 0 || (rewriteFlags & captureStartOffset) != 0 || startStrategy == PatternIndexOffsetLoweringStrategy.Zero);
                Debug.Assert((rewriteFlags & captureStartOffset) == 0 || (rewriteFlags & captureEndOffset) != 0 || endStrategy == PatternIndexOffsetLoweringStrategy.Length);
                Debug.Assert(endStrategy != PatternIndexOffsetLoweringStrategy.Length || (rewriteFlags & captureEndOffset) == 0);
                Debug.Assert((rewriteFlags & captureLength) == 0 || (rewriteFlags & useLength) != 0);

                if ((rewriteFlags & captureStartOffset) != 0)
                {
                    Debug.Assert(startMakeOffsetInput is not null);
                    if (startMakeOffsetInput.ConstantValue is null)
                    {
                        startMakeOffsetInput = F.StoreToTemp(startMakeOffsetInput, out BoundAssignmentOperator inputStore);
                        localsBuilder.Add(((BoundLocal)startMakeOffsetInput).LocalSymbol);
                        sideEffectsBuilder.Add(inputStore);
                    }
                }

                if ((rewriteFlags & captureEndOffset) != 0)
                {
                    Debug.Assert(endMakeOffsetInput is not null);
                    if (endMakeOffsetInput.ConstantValue is null)
                    {
                        endMakeOffsetInput = F.StoreToTemp(endMakeOffsetInput, out BoundAssignmentOperator inputStore);
                        localsBuilder.Add(((BoundLocal)endMakeOffsetInput).LocalSymbol);
                        sideEffectsBuilder.Add(inputStore);
                    }
                }

                BoundExpression? lengthAccess = null;

                if ((rewriteFlags & useLength) != 0)
                {
                    lengthAccess = F.Property(receiverLocal, lengthOrCountProperty);

                    if ((rewriteFlags & captureLength) != 0)
                    {
                        var lengthLocal = F.StoreToTemp(lengthAccess, out var lengthStore);
                        localsBuilder.Add(lengthLocal.LocalSymbol);
                        sideEffectsBuilder.Add(lengthStore);
                        lengthAccess = lengthLocal;
                    }
                }

                startExpr = MakePatternIndexOffsetExpression(startMakeOffsetInput, lengthAccess, startStrategy);

                if ((rewriteFlags & captureStartValue) != 0 && startExpr.ConstantValue is null)
                {
                    var startLocal = F.StoreToTemp(startExpr, out var startStore);
                    localsBuilder.Add(startLocal.LocalSymbol);
                    sideEffectsBuilder.Add(startStore);
                    startExpr = startLocal;
                }

                BoundExpression endExpr = MakePatternIndexOffsetExpression(endMakeOffsetInput, lengthAccess, endStrategy);

                if (startExpr.ConstantValue?.Int32Value == 0)
                {
                    rangeSizeExpr = endExpr;
                }
                else if (startExpr.ConstantValue is { Int32Value: var startConst } && endExpr.ConstantValue is { Int32Value: var endConst })
                {
                    rangeSizeExpr = F.Literal(unchecked(endConst - startConst));
                }
                else
                {
                    rangeSizeExpr = F.IntSubtract(endExpr, startExpr);
                }
            }
            else
            {
                var rangeLocal = F.StoreToTemp(VisitExpression(rangeArg), out var rangeStore);
                localsBuilder.Add(rangeLocal.LocalSymbol);
                sideEffectsBuilder.Add(rangeStore);

                var lengthLocal = F.StoreToTemp(F.Property(receiverLocal, lengthOrCountProperty), out var lengthStore);
                localsBuilder.Add(lengthLocal.LocalSymbol);
                sideEffectsBuilder.Add(lengthStore);

                var startLocal = F.StoreToTemp(
                    F.Call(
                        F.Call(rangeLocal, F.WellKnownMethod(WellKnownMember.System_Range__get_Start)),
                        F.WellKnownMethod(WellKnownMember.System_Index__GetOffset),
                        lengthLocal),
                    out var startStore);

                localsBuilder.Add(startLocal.LocalSymbol);
                sideEffectsBuilder.Add(startStore);
                startExpr = startLocal;

                var rangeSizeLocal = F.StoreToTemp(
                    F.IntSubtract(
                        F.Call(
                            F.Call(rangeLocal, F.WellKnownMethod(WellKnownMember.System_Range__get_End)),
                            F.WellKnownMethod(WellKnownMember.System_Index__GetOffset),
                            lengthLocal),
                        startExpr),
                    out var rangeSizeStore);

                localsBuilder.Add(rangeSizeLocal.LocalSymbol);
                sideEffectsBuilder.Add(rangeSizeStore);
                rangeSizeExpr = rangeSizeLocal;
            }

            return (BoundSequence)F.Sequence(
                localsBuilder.ToImmutableAndFree(),
                sideEffectsBuilder.ToImmutableAndFree(),
                F.Call(receiverLocal, sliceMethod, startExpr, rangeSizeExpr));
        }
    }
}
