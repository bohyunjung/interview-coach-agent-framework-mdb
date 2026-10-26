# 설정

| 설정 | 위치 | 용도 |
|---|---|---|
| `ConnectionStrings:mongodb` | AppHost user secrets | Atlas 연결 문자열. 데이터베이스 이름은 `interview_coach`(`MONGODB_DATABASE`로 변경 가능) |
| `Parameters:voyage-api-key` | AppHost user secrets | Atlas에서 발급한 Voyage AI API 키. Memory MCP에 `Voyage__ApiKey`로 전달됩니다. |
| `Voyage:Model` / `Voyage:Dimensions` | 설정 | 기본값 `voyage-4-lite` / `1024`. 벡터 인덱스의 차원과 같아야 합니다. |
| `Voyage:BaseUrl` | 설정 | 선택. 비워 두면 키 접두사로 정합니다(`al-`이면 `ai.mongodb.com`, 아니면 `api.voyageai.com`). |
| `LlmProvider` | `apphost.settings.json` | `GitHubCopilot` |
| `AgentMode` | `apphost.settings.json` | `HandOff` 또는 `Single`. [에이전트 모드](MULTI-AGENT.md) 참고 |
| `GitHubCopilot:Model` | `apphost.settings.json` | 기본값 `gpt-5-mini` |
| `GitHubCopilot:Token` | user secrets / `COPILOT_GITHUB_TOKEN` | 선택. 없으면 `gh auth login`으로 로그인한 사용자를 씁니다. |

`.env`의 값을 user secrets에 등록하는 명령입니다. `.env`가 Windows 줄바꿈(CRLF)이어도 값이 깨지지 않도록 `\r`을 먼저 제거합니다.

```sh
set -a; source <(tr -d '\r' < .env); set +a
dotnet user-secrets --file ./apphost.cs set ConnectionStrings:mongodb "$MONGODB_URI"
dotnet user-secrets --file ./apphost.cs set Parameters:voyage-api-key "$VOYAGE_API_KEY"
```

Python 스크립트는 `.env`([.env.sample](../.env.sample))에서 `MONGODB_URI`와 `VOYAGE_API_KEY`를 읽습니다. 필요하면 `MONGODB_DATABASE`, `VOYAGE_MODEL`, `VOYAGE_DIMENSIONS`, `CHAT_MESSAGES_TTL_DAYS`(기본 30일)도 지정할 수 있습니다. Atlas 네트워크와 사용자 설정은 [설정 및 실행 가이드](SETUP.md)를 보세요. `.env`와 키는 커밋하지 않습니다.
