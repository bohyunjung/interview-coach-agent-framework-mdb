"""Load the demo interview history into MongoDB Atlas.

Writes sessions (episodic), feedback_items (semantic, embedded with Voyage AI)
and the user_profile document. Re-running is safe: documents are upserted by _id
and embeddings are only recomputed when the text or model changed.

    python seed_memory.py             # seed Atlas
    python seed_memory.py --dry-run   # validate the data files, no network
    python seed_memory.py --reset     # also delete everything the live demo created
"""
import argparse
import hashlib
import json
from datetime import datetime

import config


def load(name: str):
    with open(config.DATA_DIR / name, encoding="utf-8") as f:
        return json.load(f)


def parse_date(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def validate(sessions: list, items: list, profile: dict) -> None:
    session_ids = [s["_id"] for s in sessions]
    item_ids = [i["_id"] for i in items]
    if len(set(session_ids)) != len(session_ids):
        raise ValueError("Duplicate session _id")
    if len(set(item_ids)) != len(item_ids):
        raise ValueError("Duplicate feedback item _id")

    known = set(session_ids)
    for item in items:
        if item["sessionId"] not in known:
            raise ValueError(f"{item['_id']} references unknown session {item['sessionId']}")
        if item["kind"] not in {"strength", "improvement"}:
            raise ValueError(f"{item['_id']} has invalid kind {item['kind']}")

    refs = [r for section in ("strengths", "growthAreas") for e in profile[section] for r in e["sessionIds"]]
    unknown = set(refs) - known
    if unknown:
        raise ValueError(f"Profile references unknown sessions: {sorted(unknown)}")
    if profile["interviewCount"] != len(sessions):
        raise ValueError("Profile interviewCount does not match the number of sessions")


def build_documents(sessions: list, items: list, profile: dict):
    by_id = {s["_id"]: s for s in sessions}
    session_docs = [
        {**s, "createdAt": parse_date(s["createdAt"]), "completedAt": parse_date(s["completedAt"]), "isCompleted": True}
        for s in sessions
    ]
    item_docs = []
    for item in items:
        session = by_id[item["sessionId"]]
        item_docs.append({
            **item,
            "companyName": session["companyName"],
            "roleTitle": session["roleTitle"],
            "createdAt": parse_date(session["completedAt"]),
            "textHash": hashlib.sha256(item["text"].encode()).hexdigest(),
        })
    profile_doc = {**profile, "updatedAt": parse_date(profile["updatedAt"])}
    return session_docs, item_docs, profile_doc


def embed(texts: list[str]) -> list[list[float]]:
    import voyageai

    client = voyageai.Client(api_key=config.require("VOYAGE_API_KEY"))
    result = client.embed(
        texts,
        model=config.VOYAGE_MODEL,
        input_type="document",
        output_dimension=config.EMBEDDING_DIMENSIONS,
    )
    return result.embeddings


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="validate the data files only")
    parser.add_argument("--reset", action="store_true",
                        help="delete sessions, feedback items and chat history that are not part of the seed")
    parser.add_argument("--reembed", action="store_true", help="recompute every embedding")
    args = parser.parse_args()

    sessions, items, profile = load("sessions.json"), load("feedback_items.json"), load("user_profile.json")
    validate(sessions, items, profile)
    session_docs, item_docs, profile_doc = build_documents(sessions, items, profile)

    if args.dry_run:
        print(f"OK: {len(session_docs)} sessions, {len(item_docs)} feedback items, 1 profile.")
        return

    from pymongo import MongoClient, ReplaceOne

    db = MongoClient(config.require("MONGODB_URI"))[config.DATABASE]

    if args.reset:
        seed_sessions = [d["_id"] for d in session_docs]
        seed_items = [d["_id"] for d in item_docs]
        removed = (
            db[config.SESSIONS].delete_many({"_id": {"$nin": seed_sessions}}).deleted_count,
            db[config.FEEDBACK_ITEMS].delete_many({"_id": {"$nin": seed_items}}).deleted_count,
            db[config.CHAT_MESSAGES].delete_many({}).deleted_count,
        )
        print(f"Reset: removed {removed[0]} sessions, {removed[1]} feedback items, {removed[2]} chat turns.")

    existing = {
        d["_id"]: d
        for d in db[config.FEEDBACK_ITEMS].find({}, {"textHash": 1, "embeddingModel": 1, "embedding": 1})
    }
    stale = [
        d for d in item_docs
        if args.reembed
        or d["_id"] not in existing
        or existing[d["_id"]].get("textHash") != d["textHash"]
        or existing[d["_id"]].get("embeddingModel") != config.VOYAGE_MODEL
    ]
    vectors = dict(zip((d["_id"] for d in stale), embed([d["text"] for d in stale]))) if stale else {}
    for d in item_docs:
        d["embeddingModel"] = config.VOYAGE_MODEL
        d["embedding"] = vectors.get(d["_id"]) or existing[d["_id"]]["embedding"]

    db[config.SESSIONS].bulk_write([ReplaceOne({"_id": d["_id"]}, d, upsert=True) for d in session_docs])
    db[config.FEEDBACK_ITEMS].bulk_write([ReplaceOne({"_id": d["_id"]}, d, upsert=True) for d in item_docs])
    db[config.USER_PROFILE].replace_one({"_id": profile_doc["_id"]}, profile_doc, upsert=True)

    print(f"Seeded {len(session_docs)} sessions, {len(item_docs)} feedback items "
          f"({len(stale)} embedded with {config.VOYAGE_MODEL}), 1 profile into '{config.DATABASE}'.")


if __name__ == "__main__":
    main()
