# Contributing

Keep application reference in `docs/`.

## Choose an issue form

Open this repository's [issues page](https://github.com/mongodb-developers-kr/interview-coach-agent-framework-mdb/issues), click **New issue**, and select **Bug report**, **Feature request**, **Documentation issue or request**, **Regression**, or **General request**. Use **Regression** when behavior worked in an earlier version, and include the last known working and first known failing versions. Use **General request** for questions or requests that do not fit the other categories.

## <a name="issue"></a> Report a bug

<a id="submit-issue"></a>

Search the [issues](https://github.com/mongodb-developers-kr/interview-coach-agent-framework-mdb/issues) before opening one. Include the affected checkpoint or revision, operating system, command, input, expected result, and actual result. For model behavior, include the provider, model, tool call, and relevant stored state rather than only a screenshot of the reply.

Use fictional documents and redact credentials, account details, and transcript contents from logs. Do not post real interview records.

## <a name="feature"></a> Propose a feature

Describe the user task and the contract that would change. For an agent or tool addition, explain its allowed operations, failure behavior, and how you would observe success. Discuss substantial changes in an issue before implementing them so the sample remains consistent.

## <a name="submit"></a> Submit a change

<a id="submit-pr"></a>

Make the change in a fork and open a pull request with the relevant issue, implementation notes, and checks performed. Use a descriptive commit message. Avoid unrelated formatting or dependency upgrades.

In Korean text, avoid putting a particle right after a closing `**` (for example `**기억**합니다`), because some Markdown renderers leave the markers unrendered. Prefer plain text, or end the bold span before a space or punctuation.
