# GEM300 요구사항 추적표

기준일: 2026-08-12

이 문서는 저장소 밖의 로컬 표준 원문을 읽기 전용으로 대조한 결과와 구현 경계를
기록합니다. 표준 원문, 고객사명, 내부 사양 및 장문 인용은 포함하지 않습니다.

## 판정 원칙

- 로컬에서 읽을 수 있는 base 원문은 해당 Revision의 도메인 개념에만 사용합니다.
- 표준 wire는 해당 base와 `.1` mapping 원문을 모두 확인해야 구현할 수 있습니다.
- 도메인 Unit 증거를 wire, 외부 상호운용 또는 현장 증거로 승격하지 않습니다.
- 결과 상태는 `PASS`, `BLOCKED_STANDARD`, `NOT_RUN`, `NOT_APPLICABLE`,
  `INTENTIONALLY_EXCLUDED`만 사용합니다.

## 규범 Eligibility

| Capability | 확인한 로컬 근거 | 누락 근거 | 이번 판정 |
|---|---|---|---|
| E39 / E39.1 | E39-0703 (Reapproved 1109) | E39.1 원문 | E39 도메인만 Revision-scoped `PASS`; wire `BLOCKED_STANDARD` |
| E40 / E40.1 | E40-0312 | E40.1 원문 | E40 도메인만 Revision-scoped `PASS`; wire `BLOCKED_STANDARD` |
| E87 / E87.1 | E87-0312 | E87.1 원문 | E87 도메인만 Revision-scoped `PASS`; wire `BLOCKED_STANDARD` |
| E90 / E90.1 | E90-0312 | E90.1 원문 | E90 도메인만 Revision-scoped `PASS`; wire `BLOCKED_STANDARD` |
| E94 / E94.1 | E94-0314 | E94.1 원문 | E94 도메인만 Revision-scoped `PASS`; wire `BLOCKED_STANDARD` |
| E116 / E116.1 | 없음 | base와 `.1` 원문 | Equipment Performance domain/wire `BLOCKED_STANDARD`; 공개 API 없음 |
| E42 | 없음 | 규범 원문 | Recipe 표준 주장 `BLOCKED_STANDARD`; generic Process Program을 E42로 승격하지 않음 |
| E139 | 없음 | 규범 원문 | RaP 표준 주장 `BLOCKED_STANDARD`; placeholder API 없음 |

로컬판과 현재판 사이의 변경점은 최신 원문 없이 확인할 수 없습니다. 따라서 base
도메인 `PASS`를 현재판 적합성, 인증 또는 wire 지원으로 표현하지 않습니다.

## 표준별 Domain/Wire/External Matrix

| 표준 | Domain API | Standard Wire | Unit | Loopback | External | Field |
|---|---|---|---|---|---|---|
| E39 / E39.1 | `PASS` | `BLOCKED_STANDARD` | `PASS` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E40 / E40.1 | `PASS` | `BLOCKED_STANDARD` | `PASS` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E87 / E87.1 | `PASS` | `BLOCKED_STANDARD` | `PASS` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E90 / E90.1 | `PASS` | `BLOCKED_STANDARD` | `PASS` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E94 / E94.1 | `PASS` | `BLOCKED_STANDARD` | `PASS` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E116 / E116.1 | `BLOCKED_STANDARD` | `BLOCKED_STANDARD` | `NOT_APPLICABLE` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E42 | `BLOCKED_STANDARD` | `BLOCKED_STANDARD` | `NOT_APPLICABLE` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |
| E139 | `BLOCKED_STANDARD` | `BLOCKED_STANDARD` | `NOT_APPLICABLE` | `BLOCKED_STANDARD` | `NOT_RUN` | `NOT_RUN` |

`Loopback`의 `BLOCKED_STANDARD`는 wire 경로에만 적용됩니다. 메모리 내 QuickStart는
도메인 실행 증거이며 wire loopback이 아닙니다.

## Base 도메인 요구사항과 구현 추적

| 도메인 요구사항 | 구현 경계 | 집중 자동화 증거 | 상태 |
|---|---|---|---|
| 객체 Identity와 typed attribute/action | `Gem300ObjectService` | Object/schema/action/timeout/generation tests | `PASS` |
| Manager source-of-truth Projection | `RegisterProjection`, raw mutation/removal block, typed application action | Projection reservation and routing tests | `PASS` |
| Load Port/Carrier 상태와 검증 | `CarrierManager` | 상태 전이, ID/Slot Map, 독립 Port tests | `PASS` |
| Carrier/Substrate 원자 수락·반출 | shared `Gem300DomainGate`, coordinated ownership | partial-failure, clock-failure, direct-unload tests | `PASS` |
| Substrate 위치·이력·처리 | `SubstrateTracker` | occupancy, injected time, terminal state, lease tests | `PASS` |
| Process Job Recipe/Material 무결성 | `ProcessJobManager`, retained `ProcessProgram`, substrate leases | identity mismatch, delete-vs-claim, retained-reference tests | `PASS` |
| Control Job 단독 소유와 직렬 실행 | `ControlJobManager`, central claim/execution stores | cross-manager ownership and concurrent coordinator tests | `PASS` |
| Graph Identity | concrete manager composition checks | mismatched Substrate/Process manager fail-fast tests | `PASS` |
| 실패·취소 정리 | `Gem300WorkflowCoordinator` | processor failure, cancellation, Stop/Abort non-success tests | `PASS` |
| 이벤트 Identity·보존·비투척 게시 | `Gem300EventJournal`, shared `Gem300EventPublisher` | aggregate identity, drop/health, throwing journal tests | `PASS` |
| 안정적 Snapshot | concrete manager snapshot/query members | ordinal ordering and immutable snapshot tests | `PASS` |
| 명시적 Slot 연결 | `CarrierSubstrateSlotAssignment`, five-argument `CarrierArrivalPlan` | ambiguous-order rejection and query tests | `PASS` |

집중 증거는 `Gem300ModelTests`, `CarrierManagerTests`, `SubstrateTrackerTests`,
`JobManagerTests`, `ObjectAndEventTests`, `WorkflowTests`,
`IntegrityRegressionTests`, `ExtendedIntegrityTests`, `ClosureRemediationTests`,
`LatestAuditRemediationTests`에 연결됩니다. 최종 전체 Test Count는 중앙 제품화
보고서의 fresh Release 검증에서만 기록합니다.

## 호환성과 제외 범위

- 기존 공개 Interface와 Constructor는 유지하고 모델 및 concrete 진단 API를
  additive로 추가했습니다.
- 임의 external `ISubstrateTracker`/`IProcessJobManager` 조립은 원자적 소유권을
  보장할 수 없어 기존 Signature에서 fail-fast하도록 동작을 강화했습니다.
- Control Job Abort는 기존 `Completed` terminal을 유지합니다. 별도 Enum 값 추가는
  breaking change이므로 현재 범위에서 발명하지 않습니다.
- 영속 저장, 재시작/장애 복구, 분산 Transaction 및 프로세스 간 소유권은
  `INTENTIONALLY_EXCLUDED`입니다.
- E84 Handoff와 Host↔Equipment 재연결 동기화는 이 도메인 Gate에서
  `INTENTIONALLY_EXCLUDED`입니다.
- Standard wire fixture, TCP loopback, 독립 Simulator 및 실장비 시험은 mapping 원문
  부재로 구현하지 않았으며 External/Field 결과는 `NOT_RUN`입니다.
