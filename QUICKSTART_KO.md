# 빠른 시작

저장소의 샘플은 기본적으로 공개 `Dreamine.Gem300` 패키지를 참조하므로 독립 Clone에서 실행됩니다. 저장소 디렉터리에서 빌드 가능한 메모리 기반 Workflow를 실행합니다.

```powershell
dotnet run --project samples/Dreamine.Gem300.QuickStart
```

Canonical Full Source Workspace를 검증할 때만 `-p:UseLocalDreamineSources=true`를 추가하십시오.

샘플은 Recipe 등록과 `Gem300Runtime` 소비에 하나의
`GemProcessProgramService` Instance를 공유합니다. 이후 다음을 수행합니다.

1. Load Port를 등록하고 Service 상태로 전환합니다.
2. 애플리케이션이 Slot↔Substrate 연결을 명시한 Carrier를 수락합니다.
3. Process Job과 Control Job을 생성합니다.
4. 순서화된 Job을 실행하고 Substrate를 목적지로 이동합니다.
5. Job을 삭제하고 조정 중인 Carrier를 원자적으로 반출합니다.

구체 `Dreamine.Gem.GemRuntime`을 이미 소유한 애플리케이션은 GEM300을 그
Runtime의 Process Program Store에 연결하는 Factory를 사용합니다.

```csharp
var gem300 = Gem300Runtime.CreateFromGemRuntime(gemRuntime);
gemRuntime.ProcessPrograms.Put(new GemProcessProgram("RECIPE-1", [0x01]));
```

집중 검증한 메모리 내 도메인 경로는 `PASS`입니다. 이 샘플은 HSMS 연결을 열지
않고 GEM300 SECS-II 메시지를 구현하지 않습니다. 관련 `.1` wire mapping은
`BLOCKED_STANDARD`, 외부 및 현장 검증은 `NOT_RUN`입니다. 영속 저장, 재시작
복구 및 프로세스 간 소유권은 `INTENTIONALLY_EXCLUDED`입니다.

생산 통합 전에 [알려진 제한](KNOWN_LIMITATIONS.md)을 확인하십시오.
