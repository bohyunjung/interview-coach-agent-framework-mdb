# 준비 사항

메모리는 MongoDB Atlas에 저장합니다. 무료 M0 클러스터로 충분합니다. 피드백 항목의 임베딩은 Atlas에서 발급한 Voyage AI API 키로 만듭니다. 앱 외에 로컬에서 따로 실행할 서버는 없습니다.

## 1. Atlas 클러스터 만들기

1. <https://www.mongodb.com/cloud/atlas/register>에서 가입하고 프로젝트를 만듭니다.
2. **Create a cluster** 버튼을 눌러 **M0 (Free)** 티어를 선택합니다. 클라우드와 리전은 아무거나 괜찮습니다.
3. **Network Access** 메뉴에서 Add IP Address를 눌러 현재 IP를 추가합니다. 데브컨테이너나 Codespace에서는 공인 IP를 허용해야 합니다. `0.0.0.0/0`(모든 IP 허용)은 짧은 데모에서만 쓰세요.
4. **Database Access** 메뉴에서 Add New Database User를 눌러 비밀번호 인증 사용자를 만듭니다.
   - `interview-coach-app`: Read and write to any database 역할 (또는 `interview_coach` 데이터베이스로 제한)
   - `interview-coach-readonly`(선택, MongoDB MCP Server용): Read any database 역할
5. **Connect** 메뉴에서 Drivers를 선택하고, 연결 문자열(`mongodb+srv://...`)을 복사해 `<password>`를 사용자 비밀번호로 바꿉니다.

## 2. Voyage AI API 키 받기

임베딩 모델(Voyage AI)은 Atlas에서 발급한 Model API 키로 사용합니다.

1. Atlas에서 위에서 만든 프로젝트를 엽니다.
2. 프로젝트 화면의 왼쪽 메뉴에서 **AI Model APIs** 항목을 선택합니다. 조직 화면에서는 Services 아래의 Model API Keys입니다.
3. **Create model API key** 버튼을 누르고, 키 이름을 입력한 뒤 Create를 누릅니다.
4. 화면에 표시된 키를 복사해 둡니다. 이 화면을 벗어나면 키를 다시 볼 수 없습니다. 잃어버리면 새로 만들어야 합니다.

이 키는 `al-`로 시작하고, 요청은 `https://ai.mongodb.com/v1`로 갑니다. 앱과 스크립트가 접두사를 보고 알아서 이 주소를 씁니다. 앱은 `voyage-4-lite` 모델(1024차원)을 씁니다.

## 3. 시크릿 설정

```bash
cp .env.sample .env      # .env는 git에서 제외됩니다
```

`.env`에 `MONGODB_URI`와 `VOYAGE_API_KEY`를 채웁니다. Python 스크립트는 이 파일을 읽습니다. .NET AppHost는 user secrets를 읽으므로, 같은 값을 아래 명령으로 등록합니다.

```bash
set -a; source <(tr -d '\r' < .env); set +a   # tr은 Windows 줄바꿈(\r)을 제거합니다
dotnet user-secrets --file ./apphost.cs set ConnectionStrings:mongodb "$MONGODB_URI"
dotnet user-secrets --file ./apphost.cs set Parameters:voyage-api-key "$VOYAGE_API_KEY"
dotnet user-secrets --file ./apphost.cs list | sed 's/=.*/= ***/'   # 등록 확인. 값은 가립니다
```

Copilot SDK가 계정에 접근할 수 있도록 한 번 로그인합니다.

```bash
gh auth login
```

## 4. 샘플 데이터 넣기

데브컨테이너에는 Python 의존성(`scripts/memory/requirements.txt`)이 미리 설치돼 있습니다. 각 단계가 하는 일을 확인할 수 있도록 하나씩 실행합니다.

```bash
python scripts/memory/seed_memory.py --dry-run   # 데이터 파일만 검증합니다. 네트워크를 쓰지 않습니다
python scripts/memory/create_indexes.py          # 컬렉션, 인덱스, 벡터 인덱스를 만듭니다
python scripts/memory/seed_memory.py             # 면접 세션, 피드백 항목(임베딩 포함), 프로필을 저장합니다
```

두 스크립트는 여러 번 실행해도 데이터가 중복되지 않습니다. 벡터 인덱스는 만든 뒤 검색에 쓸 수 있을 때까지 1분쯤 걸리며, `create_indexes.py`가 끝날 때까지 기다립니다. 데브컨테이너 밖에서 실행한다면 먼저 `pip install -r scripts/memory/requirements.txt`를 실행하세요.

데모에서 만든 데이터를 지우고 샘플 데이터만 남기려면 `python scripts/memory/seed_memory.py --reset`을 실행합니다.

## 5. (선택) Copilot Chat에서 메모리 들여다보기

[.vscode/mcp.json](../.vscode/mcp.json)에 공식 [MongoDB MCP Server](https://github.com/mongodb-js/mongodb-mcp-server)가 `--readOnly`로 등록돼 있습니다. Copilot Chat에서 에이전트가 저장한 데이터를 조회할 수 있고, 수정은 할 수 없습니다. `npx`로 실행되며 데브컨테이너에는 Node.js가 설치돼 있습니다.

1. Copilot Chat을 Agent 모드로 열고, 도구 선택기에서 `mongodb` 서버를 시작합니다.
2. 연결 문자열을 물으면 `.env`의 `MONGODB_URI`를 붙여 넣습니다. 데이터베이스에서도 읽기 전용을 강제하려면 위에서 만든 `interview-coach-readonly` 사용자의 연결 문자열을 쓰세요.
3. 질문해 봅니다. 예: "`interview_coach`의 컬렉션별 문서 수를 알려줘." 더 많은 예시는 [시나리오 4](DEMO.md#시나리오-4-읽기-전용-mongodb-mcp-server로-데이터-들여다보기)에 있습니다.

데브컨테이너에는 MongoDB for VS Code Extension도 설치돼 있습니다. 이 Extension은 사람이 컬렉션을 GUI로 살펴보고 쿼리를 실행하는 도구이고, MCP Server와는 별개로 동작합니다.

## 그 밖에 필요한 것

- .NET 10 SDK
- Docker 같은 컨테이너 엔진 (MarkItDown MCP Server가 컨테이너로 실행됩니다)
- GitHub Copilot 사용 권한
- Python 3.10 이상 (스크립트용, 데브컨테이너에 포함)
- Node.js (MongoDB MCP Server용, 데브컨테이너에 포함)
