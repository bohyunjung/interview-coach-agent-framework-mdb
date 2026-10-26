# 아키텍처

| 구성 요소 | 프로젝트 | 역할 |
|---|---|---|
| AppHost | `src/InterviewCoach.AppHost`, 루트 `apphost.cs` | Aspire 리소스 구성. 두 파일은 항상 같은 내용으로 유지합니다. |
| WebUI | `InterviewCoach.WebUI` | Blazor 채팅 UI. AG-UI로 에이전트와 통신하고, 저장된 대화를 읽어 복원합니다. |
| Agent | `InterviewCoach.Agent` | GitHub Copilot SDK 위에서 동작하는 Agent Framework 워크플로. Short Term 미들웨어 포함. |
| Memory MCP | `InterviewCoach.Mcp.Memory` | MongoDB에 접근하는 MCP 도구 (`/mcp`, stateless HTTP) |
| Memory 라이브러리 | `InterviewCoach.Memory` | MongoDB C# 드라이버 기반 리포지토리, Voyage 임베딩 클라이언트, 모델 |
| MarkItDown MCP | 컨테이너 `mcp/markitdown` | 이력서와 JD에서 텍스트 추출 |
| MongoDB Atlas | 외부 | 메모리 저장소. 컬렉션은 [MEMORY.md](MEMORY.md)를 참고 |
| Voyage API | 외부 | 피드백 항목과 검색어의 임베딩 생성 |

```
브라우저 → WebUI → (AG-UI) → Agent ─┬─ Copilot SDK (모델)
                                    ├─ MarkItDown MCP
                                    ├─ Memory MCP ──────────┐
                                    └─ Short Term 미들웨어 ─┤→ MongoDB Atlas (+ Voyage)
```

로컬 포트: WebUI `5087`, Agent `5152` (`/ag-ui`, `/devui/`), Memory MCP `5141` (`/mcp`). MarkItDown은 무작위 포트로 노출됩니다.

## 데이터 흐름

- 연결 문자열과 키: Agent와 Memory MCP가 `mongodb` 연결 문자열을 참조합니다. Voyage 키는 임베딩을 만드는 Memory MCP에만 전달합니다.
- Short Term 저장: UI는 매 턴마다 전체 메시지 이력을 보냅니다. 그래서 미들웨어는 마지막 사용자 메시지와 스트리밍된 응답만 기록합니다. 세션은 시스템 메시지의 `SessionId: <guid>`로 구분합니다.
- 대화 복원: WebUI 주소는 `/chat/{sessionId}`입니다. 페이지가 열리면 Agent의 `GET /chat/{sessionId}/messages`를 호출해 `chat_messages`의 턴을 읽어 옵니다. 여러 에이전트가 이어서 한 답변은 하나로 합쳐 돌려줍니다. 그래서 새로고침, 새 탭, WebUI 재시작 후에도 대화가 이어집니다.
- 업로드: 첨부 파일은 Agent가 `/uploads/...` URL로 제공하고, MarkItDown 컨테이너가 이를 가져갑니다. 컨테이너에서 접근할 수 있도록 loopback 주소는 `host.docker.internal`로 바꿉니다.
- Voyage 엔드포인트: Atlas에서 발급한 키(`al-` 접두사)는 `https://ai.mongodb.com/v1`로 요청합니다. Voyage AI 대시보드에서 직접 발급한 키는 `https://api.voyageai.com/v1`로 요청하며, `Voyage:BaseUrl`로 바꿀 수 있습니다.

`scripts/memory/`(Python)는 인덱스를 만들고 샘플 데이터를 저장합니다. .NET 프로젝트와는 독립적으로 동작합니다.
