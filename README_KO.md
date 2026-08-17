# Dreamine.Gem300

[![CI](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/actions/workflows/ci.yml/badge.svg)](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/actions/workflows/ci.yml)
[![품질 게이트](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300) [![보안 등급](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300) [![테스트 커버리지](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=coverage)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300)

`Dreamine.Gem300`은 현대 .NET 애플리케이션을 위한 hardened 프로세스 내
GEM300 도메인 경계를 구현합니다.

[➡️ English Version](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/README.md)

## 설치와 시작

```powershell
dotnet add package Dreamine.Gem300
```

프로세스 내 Carrier, Substrate, Process Job, Control Job Workflow가 필요할 때 선택합니다. GEM300 `.1` Wire Mapping은 제공하지 않습니다. [Package-first QuickStart](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/QUICKSTART_KO.md)를 실행하고, Full Source Workspace를 검증할 때만 `-p:UseLocalDreamineSources=true`를 사용하십시오.

## 증거 상태

| 기능 | 상태 | 증거 |
|---|---|---|
| E39/E40/E87/E90/E94 메모리 내 도메인 경계 | `PASS` | 모델·Manager·Workflow·무결성·동시성·취소·회귀 집중 테스트 |
| E39.1/E40.1/E87.1/E90.1/E94.1 표준 wire binding | `BLOCKED_STANDARD` | 필요한 mapping 원문을 로컬에서 확보하지 못함 |
| E116/E116.1, E42 및 E139 주장 | `BLOCKED_STANDARD` | 필요한 규범 원문을 확보하지 못함 |
| 외부 상호운용 및 현장 증거 | `NOT_RUN` | 독립 counterpart 또는 생산 장비 증거를 실행하지 않음 |
| 영속 저장, 재시작 복구 및 프로세스 간 소유권 | `INTENTIONALLY_EXCLUDED` | 이 제품화 Gate는 프로세스 내 메모리 범위로 명시함 |

`PASS`는 구현한 로컬 base Revision 도메인 경계에만 적용됩니다. 현재판 적합성,
인증, 표준 wire 지원 또는 벤더 상호운용을 의미하지 않습니다.

## 구현한 도메인 경계

- E39 방식 객체 Identity, 형식화된 RO/RW 속성, 취소 가능한 Action 및 애플리케이션 선언 Manager Projection
- Projection Key 예약, Source-of-truth 읽기, raw mutation/removal 차단 및 typed application action routing
- 원자적 Carrier/Substrate 수락·반출을 포함한 E87 Load Port와 Carrier 상태
- E90 Substrate 위치 점유, 체류 이력, 처리 상태 및 참조 Lease
- 보존 Process Program Identity와 Material Lease를 포함한 E40 Process Job 생명주기
- 중앙 Process Job 소유권과 공유 Execution Claim을 포함한 E94 직렬 Control Job Queue
- 애플리케이션이 명시하는 Carrier Slot↔Substrate 연결(순서나 위치 문자열을 Slot Index로 추론하지 않음)
- 안정적 Snapshot과 공유 Graph Identity 검증(호환되지 않는 built-in Manager Graph는 fail-fast)
- Journal Identity와 Drop/Retention Health 및 공유 비투척 Publisher를 제공하는 제한 용량 프로세스 내 Event Journal
- 실패 정리에서 전진 가능한 상태 전이만 사용하는 Experimental Carrier→Process→반출 Coordinator(Stopped/Aborted Process Job을 성공 Substrate 또는 Control Job 완료로 승격하지 않음)

## 안전한 조립

구체 `Dreamine.Gem.GemRuntime`을 사용할 때는 두 계층이 같은 Process Program
Store를 사용하도록 해당 Runtime에서 GEM300을 생성합니다.

```csharp
var gem300 = Gem300Runtime.CreateFromGemRuntime(gemRuntime);
gemRuntime.ProcessPrograms.Put(new GemProcessProgram("RECIPE-1", [0x01]));

gem300.Carriers.RegisterLoadPort("PORT-1");
gem300.Carriers.SetInService("PORT-1");
gem300.Workflow.AcceptCarrier(new CarrierArrivalPlan(
    "PORT-1",
    "CARRIER-1",
    [CarrierSlotState.CorrectlyOccupied],
    [new SubstrateArrivalPlan("SUBSTRATE-1", "SOURCE-1", "DESTINATION-1")],
    [new CarrierSubstrateSlotAssignment(0, "SUBSTRATE-1")]));
```

호환 조립을 위해 기존
`Gem300Runtime(IGemRuntime, IGemProcessProgramService, ...)` Constructor를
유지했습니다. 호출자는 GEM 계층이 사용하는 것과 동일한 논리 Process Program
Service를 전달해야 합니다. QuickStart는 한 Service Instance를 명시적으로 공유하며,
생산용 구체 Runtime에서는 `CreateFromGemRuntime`을 권장합니다.

명시적 Slot 연결과 Object Projection Key는 애플리케이션 통합 메타데이터입니다.
발명한 `.1` wire mapping이 아닙니다.

## 문서

- [빠른 시작](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/QUICKSTART_KO.md)
- [알려진 제한](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/KNOWN_LIMITATIONS.md)
- [공개 API 검토](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/docs/API_REVIEW.md)
- [SEMI 요구사항 추적](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/docs/SEMI_REQUIREMENTS_TRACE.md)

## 라이선스

MIT.
