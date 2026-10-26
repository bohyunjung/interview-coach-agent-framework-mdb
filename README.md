# Interview Coach: MongoDB로 만든 에이전트 메모리

[Azure-Samples/interview-coach-agent-framework](https://github.com/Azure-Samples/interview-coach-agent-framework)를 포크해서, 에이전트의 메모리 저장소를 MongoDB Atlas로 바꾼 데모입니다. 앱은 GitHub Copilot SDK, Microsoft Agent Framework, MCP, Aspire로 만들었습니다.

코치는 이력서와 직무기술서(JD)를 바탕으로 모의 면접을 진행하고 피드백을 줍니다. 면접 내용은 모두 저장되기 때문에 나중에 이런 질문을 할 수 있습니다.

- "Kestrel 면접이 언제였지?"
- "최근 5건 면접에서 공통으로 나온 피드백이 뭐야?"
- "내 디버깅 역량을 칭찬한 회사가 어디였지?"

## 메모리 구성

에이전트 메모리를 네 종류로 나누고, 종류마다 컬렉션 하나에 저장합니다.

| 메모리 | 컬렉션 | 담는 내용 | 누가 저장하나 | 어떻게 꺼내나 |
|---|---|---|---|---|
| Short Term (단기) | `chat_messages` | 진행 중인 대화의 발화 | 앱의 미들웨어가 매 턴 자동 저장 (LLM 판단과 무관) | 대화 ID로 조회. 새로고침하면 대화가 복원되고, 오래된 대화는 TTL로 삭제 (기본 30일) |
| Episodic (일화) | `sessions` | 면접 한 건의 회사, 직무, 대화록, 요약 | 면접 중에 에이전트가 도구를 호출해 저장 | 회사명, 날짜, 최신순으로 필터링 |
| Semantic (의미): 피드백 항목 | `feedback_items` | 면접에서 나온 강점과 개선점. 항목 하나가 문서 하나이고 임베딩을 포함 | 면접이 끝나면 `summariser` 에이전트가 저장 | MongoDB Vector Search on Atlas로 의미 검색. 메타데이터 필터와 함께 사용 |
| Semantic (의미): 사용자 프로필 | `user_profile` | 지난 면접 전체를 종합한 강점, 개선점, 면접 횟수 | 면접이 끝날 때마다 `summariser`가 다시 작성 | 문서 하나를 그대로 조회 |

메모리별 설계는 [docs/MEMORY.md](docs/MEMORY.md), 시연 순서와 프롬프트는 [docs/DEMO.md](docs/DEMO.md)에 정리했습니다.

## 빠른 시작

1. [준비 사항](docs/PREREQUISITES.md)을 확인합니다. VS Code에서 저장소를 열고 **Reopen in Container**를 선택하는 것을 권장합니다.
2. [설정 및 실행 가이드](docs/SETUP.md)를 따라 Atlas와 Voyage AI를 설정하고, 시크릿 등록, 샘플 데이터 준비, 앱 실행을 진행합니다.

   샘플 데이터를 직접 준비하려면 다음 명령을 사용합니다. Python 의존성은 Dev Container에 미리 설치돼 있습니다.
   ```sh
   cp .env.sample .env   # MONGODB_URI와 VOYAGE_API_KEY 값을 채웁니다
   python scripts/memory/create_indexes.py && python scripts/memory/seed_memory.py
   ```

Web UI는 모든 필수 시크릿을 등록하고 Aspire 리소스가 실행된 뒤 <http://localhost:5087>에서 열립니다. 접속이 되지 않으면 먼저 Aspire 대시보드에서 `mcp-memory`, `agent`, `webui` 상태를 확인하세요. [samples/](samples/)의 가상 이력서로 면접을 진행하고, 면접이 끝나면 지난 면접에 대해 물어볼 수 있습니다.
