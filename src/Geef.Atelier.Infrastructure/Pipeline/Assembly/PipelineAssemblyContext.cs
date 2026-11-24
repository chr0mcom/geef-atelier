using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>Everything an <see cref="IPipelineAssembler"/> needs to build the pipeline of one run.</summary>
internal sealed record PipelineAssemblyContext(
    RunEntity Run,
    CrewSnapshot Snapshot,
    IServiceProvider RunServices,
    IServiceScopeFactory ScopeFactory,
    ILlmClientResolver LlmClientResolver,
    IOptions<ConvergenceOptions> ConvergenceOptions,
    ILoggerFactory LoggerFactory,
    IGeefEventSink EventSink,
    ICostAccumulator? CostAccumulator,
    IRunWarningSink WarningSink,
    IGroundingProviderFactory? GroundingProviderFactory,
    IPricingCatalog? PricingCatalog);
