"""Create the collections and indexes used by the memory layer.

Regular indexes support the episodic lookups and short-term history; a TTL index
expires old chat messages; a MongoDB Vector Search on Atlas index serves semantic
recall over feedback_items. Safe to re-run.
"""
import time

from pymongo import ASCENDING, DESCENDING, MongoClient
from pymongo.operations import SearchIndexModel

import config

VECTOR_INDEX_TIMEOUT_SECONDS = 300


def ensure_collections(db) -> None:
    existing = set(db.list_collection_names())
    for name in (config.SESSIONS, config.FEEDBACK_ITEMS, config.USER_PROFILE, config.CHAT_MESSAGES):
        if name not in existing:
            db.create_collection(name)


def ensure_regular_indexes(db) -> None:
    db[config.SESSIONS].create_index([("createdAt", DESCENDING)])
    db[config.SESSIONS].create_index([("companyName", ASCENDING), ("createdAt", DESCENDING)])
    db[config.FEEDBACK_ITEMS].create_index([("sessionId", ASCENDING)])
    db[config.FEEDBACK_ITEMS].create_index([("kind", ASCENDING), ("createdAt", DESCENDING)])
    db[config.CHAT_MESSAGES].create_index([("conversationId", ASCENDING), ("createdAt", ASCENDING)])
    db[config.CHAT_MESSAGES].create_index(
        [("createdAt", ASCENDING)],
        name="chat_messages_ttl",
        expireAfterSeconds=config.CHAT_TTL_DAYS * 24 * 3600,
    )


def ensure_vector_index(db) -> None:
    collection = db[config.FEEDBACK_ITEMS]
    if any(ix["name"] == config.VECTOR_INDEX for ix in collection.list_search_indexes()):
        print(f"Vector index '{config.VECTOR_INDEX}' already exists.")
    else:
        collection.create_search_index(SearchIndexModel(
            name=config.VECTOR_INDEX,
            type="vectorSearch",
            definition={"fields": [
                {"type": "vector", "path": "embedding", "numDimensions": config.EMBEDDING_DIMENSIONS, "similarity": "cosine"},
                {"type": "filter", "path": "kind"},
                {"type": "filter", "path": "companyName"},
                {"type": "filter", "path": "roleTitle"},
            ]},
        ))
        print(f"Creating vector index '{config.VECTOR_INDEX}'...")

    deadline = time.monotonic() + VECTOR_INDEX_TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        ready = [ix for ix in collection.list_search_indexes(config.VECTOR_INDEX) if ix.get("queryable")]
        if ready:
            print("Vector index is queryable.")
            return
        time.sleep(5)
    raise SystemExit("Timed out waiting for the vector index. Check its status in the Atlas UI.")


def main() -> None:
    db = MongoClient(config.require("MONGODB_URI"))[config.DATABASE]
    ensure_collections(db)
    ensure_regular_indexes(db)
    ensure_vector_index(db)
    print(f"Indexes ready in '{config.DATABASE}'.")


if __name__ == "__main__":
    main()
