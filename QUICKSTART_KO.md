# 빠른 시작

빌드 가능한 메모리 기반 Carrier → Substrate → Process Job → Control Job Workflow를 실행합니다.

```powershell
dotnet run --project samples/Dreamine.Gem300.QuickStart
```

Carrier/Slot 계획 검증, Material 등록, 순서화된 Job 1개 실행, 목적지 이동 및 Carrier 반출을 수행합니다. Process-local 이벤트 저널과 메모리 서비스를 사용합니다.

이 조정자는 Experimental이며 GEM300 SECS-II wire mapping, 영속성, 장애 복구 또는 외부 상호운용을 주장하지 않습니다. [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md)를 확인하십시오.
