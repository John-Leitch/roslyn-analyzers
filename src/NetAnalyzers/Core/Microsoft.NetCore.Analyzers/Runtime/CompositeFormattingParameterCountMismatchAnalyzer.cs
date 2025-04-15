// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Analyzer.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeQuality.Analyzers.MicrosoftCodeQualityAnalyzersResources;

namespace Microsoft.NetCore.Analyzers.Runtime
{

    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public sealed class CompositeFormattingParameterCountMismatchAnalyzer : DiagnosticAnalyzer
    {
        internal const string CA2025RuleId = "CA2025";

        internal static readonly DiagnosticDescriptor CA2025Rule = DiagnosticDescriptorHelper.Create(
            CA2025RuleId,
            CreateLocalizableResourceString(nameof(LoggerMessageDiagnosticFormatParameterCountMismatchTitle)),
            CreateLocalizableResourceString(nameof(LoggerMessageDiagnosticFormatParameterCountMismatchMessage)),
            DiagnosticCategory.Reliability,
            RuleLevel.BuildWarning,
            description: CreateLocalizableResourceString(nameof(LoggerMessageDiagnosticFormatParameterCountMismatchDescription)),
            isPortedFxCopRule: false,
            isDataflowRule: false,
            isReportedAtCompilationEnd: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(CA2025Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(context =>
            {
                var wellKnownTypeProvider = WellKnownTypeProvider.GetOrCreate(context.Compilation);

                if (!wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(
                        WellKnownTypeNames.SystemString,
                        out var systemStringType) ||
                    !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(
                        WellKnownTypeNames.SystemConsole,
                        out var systemConsoleType) ||
                    !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(
                        WellKnownTypeNames.SystemObject,
                        out var systemObjectType) ||
                    !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(
                        WellKnownTypeNames.SystemIOTextWriter,
                        out var systemIOTextWriterType) ||
                    !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(
                        WellKnownTypeNames.SystemTextStringBuilder,
                        out var systemTextStringBuilderType))
                {
                    return;
                }

                context.RegisterOperationAction(
                    context => AnalyzeInvocation(
                        context,
                        (IInvocationOperation)context.Operation,
                        systemStringType,
                        systemConsoleType,
                        systemIOTextWriterType,
                        systemTextStringBuilderType,
                        systemObjectType),
                    OperationKind.Invocation);
            });
        }

        private bool IsFormatInvocation(
            IInvocationOperation invocation,
            INamedTypeSymbol systemStringType,
            INamedTypeSymbol systemConsoleType,
            INamedTypeSymbol systemIOTextWriterType,
            INamedTypeSymbol systemTextStringBuilderType)
        {
            var methodSymbol = invocation.TargetMethod;
            var containingType = methodSymbol.ContainingType;

            if ((containingType.Equals(systemConsoleType, SymbolEqualityComparer.Default) ||
                containingType.Equals(systemIOTextWriterType, SymbolEqualityComparer.Default)) &&
                methodSymbol.Name is (nameof(Console.WriteLine)) or (nameof(Console.Write)))
            {
                return true;
            }
            else if (containingType.Equals(systemStringType, SymbolEqualityComparer.Default) &&
                methodSymbol.Name == nameof(string.Format))
            {
                return true;
            }
            else if (containingType.Equals(systemTextStringBuilderType, SymbolEqualityComparer.Default) &&
                methodSymbol.Name == nameof(StringBuilder.AppendFormat))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void AnalyzeInvocation(
            OperationAnalysisContext context,
            IInvocationOperation invocation,
            INamedTypeSymbol systemStringType,
            INamedTypeSymbol systemConsoleType,
            INamedTypeSymbol systemIOTextWriterType,
            INamedTypeSymbol systemTextStringBuilderType,
            INamedTypeSymbol systemObjectType)
        {
            if (!IsFormatInvocation(
                invocation,
                systemStringType,
                systemConsoleType,
                systemIOTextWriterType,
                systemTextStringBuilderType))
            {
                return;
            }

            var indexedFormatString = TryGetFormatString(invocation, systemStringType);

            if (indexedFormatString is null)
            {
                return;
            }

            var (formatString, formatStringIndex) = indexedFormatString.Value;

            var formatItemCount = FormatHelper.CountFormatItems(formatString);

            if (!TryGetFormatArgumentCount(invocation, formatStringIndex, systemObjectType, out var formatArgumentCount))
            {
                return;
            }
        }

        private (string format, int index)? TryGetFormatString(
            IInvocationOperation invocation,
            INamedTypeSymbol systemStringType)
        {
            var indexedFormatArgument = invocation.Arguments
                .Select((x, i) => new { Argument = x, Index = i })
                .FirstOrDefault(x =>
                    systemStringType.Equals(x.Argument.Parameter?.Type, SymbolEqualityComparer.Default) &&
                    x is { Argument.Parameter.Name: "format" });

            if (indexedFormatArgument is null)
            {
                return null;
            }

            var format = StringConstantEvaluator.TryEvaluate(indexedFormatArgument.Argument.Value);

            return format is not null ? (format, indexedFormatArgument.Index) : null;
        }

        private bool TryGetFormatArgumentCount(
            IInvocationOperation invocation,
            int formatIndex,
            INamedTypeSymbol systemObjectType,
            out int count)
        {
            var nullableSystemObjectType = systemObjectType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var formatArguments = invocation.Arguments.Skip(formatIndex + 1).ToImmutableArray();

            if (formatArguments is [{ Value: IArrayCreationOperation { DimensionSizes: [{ ConstantValue: { HasValue: true, Value: int size } }] } }])
            {
                count = size;

                return true;
            }
            else if (formatArguments.All(x =>
                x.Parameter is not null &&
                (x.Parameter.Type.Equals(systemObjectType, SymbolEqualityComparer.Default) ||
                x.Parameter.Type.Equals(nullableSystemObjectType, SymbolEqualityComparer.Default))))
            {
                count = formatArguments.Count();

                return true;
            }

            count = -1;
            return false;
        }
    }
}
