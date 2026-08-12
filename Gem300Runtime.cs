using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Carrier;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.ObjectServices;
using Dreamine.Gem300.Substrate;

namespace Dreamine.Gem300;

/// <summary>\if KO E39/E40/E87/E90/E94 기반 독립 모듈을 조립하는 GEM300 런타임입니다. \endif \if EN Composes independent E39/E40/E87/E90/E94-based GEM300 modules. \endif</summary>
public sealed class Gem300Runtime : IGem300Runtime
{
    /// <summary>\if KO 구체 GEM 런타임이 소유한 동일 Process Program 저장소를 사용하여 GEM300 런타임을 만듭니다. \endif \if EN Creates a GEM300 runtime using the same process-program store owned by a concrete GEM runtime. \endif</summary>
    public static Gem300Runtime CreateFromGemRuntime(Dreamine.Gem.GemRuntime gemRuntime, TimeProvider? timeProvider = null, int eventCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(gemRuntime);
        return new(gemRuntime, gemRuntime.ProcessPrograms, timeProvider, eventCapacity);
    }

    /// <summary>\if KO 기반 GEM 런타임, 공정 프로그램 경계, 시간 및 이벤트 용량으로 런타임을 만듭니다. \endif \if EN Creates a runtime with a GEM runtime, process-program boundary, time, and event capacity. \endif</summary>
    public Gem300Runtime(IGemRuntime gemRuntime, IGemProcessProgramService processPrograms, TimeProvider? timeProvider = null, int eventCapacity = 4096)
    {
        GemRuntime = gemRuntime ?? throw new ArgumentNullException(nameof(gemRuntime)); ArgumentNullException.ThrowIfNull(processPrograms);
        Events = new(timeProvider, eventCapacity); EventPublisher = new(Events, timeProvider);
        var domainGate = new Gem300DomainGate();
        Objects = new(EventPublisher, timeProvider); Carriers = new(EventPublisher, domainGate); Substrates = new(EventPublisher, timeProvider, domainGate);
        ProcessJobs = new(Substrates, processPrograms, EventPublisher); ControlJobs = new(ProcessJobs, EventPublisher); Workflow = new(Carriers, Substrates, ProcessJobs, ControlJobs);
    }
    /// <inheritdoc />
    public IGemRuntime GemRuntime { get; }
    /// <summary>\if KO 제한 용량 도메인 이벤트 저널입니다. \endif \if EN Gets the bounded domain-event journal. \endif</summary>
    public Gem300EventJournal Events { get; }
    /// <summary>\if KO 모든 기본 모듈이 공유하는 비차단 이벤트 게시기입니다. \endif \if EN Gets the non-throwing event publisher shared by all built-in modules. \endif</summary>
    public Gem300EventPublisher EventPublisher { get; }
    /// <summary>\if KO 공유 이벤트 게시기의 현재 실패 상태입니다. \endif \if EN Gets the current shared event-publisher failure health. \endif</summary>
    public Dreamine.Gem300.Abstractions.Model.Gem300EventPublisherHealth EventHealth => EventPublisher.GetHealth();
    /// <summary>\if KO 객체 서비스 저장소입니다. \endif \if EN Gets the object service store. \endif</summary>
    public Gem300ObjectService Objects { get; }
    /// <summary>\if KO Carrier 관리자입니다. \endif \if EN Gets the carrier manager. \endif</summary>
    public CarrierManager Carriers { get; }
    /// <summary>\if KO Substrate 추적기입니다. \endif \if EN Gets the substrate tracker. \endif</summary>
    public SubstrateTracker Substrates { get; }
    /// <summary>\if KO Process Job 관리자입니다. \endif \if EN Gets the process-job manager. \endif</summary>
    public ProcessJobManager ProcessJobs { get; }
    /// <summary>\if KO Control Job 관리자입니다. \endif \if EN Gets the control-job manager. \endif</summary>
    public ControlJobManager ControlJobs { get; }
    /// <summary>\if KO Experimental 통합 조정자입니다. \endif \if EN Gets the experimental workflow coordinator. \endif</summary>
    public Gem300WorkflowCoordinator Workflow { get; }
    IGem300ObjectService IGem300Runtime.Objects => Objects;
    ICarrierManager IGem300Runtime.Carriers => Carriers;
    ISubstrateTracker IGem300Runtime.Substrates => Substrates;
    IProcessJobManager IGem300Runtime.ProcessJobs => ProcessJobs;
    IControlJobManager IGem300Runtime.ControlJobs => ControlJobs;
    IGem300EventJournal IGem300Runtime.Events => Events;
}
