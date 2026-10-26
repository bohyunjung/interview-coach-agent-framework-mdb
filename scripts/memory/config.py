"""Shared settings for the memory seed and index scripts."""
import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[2]
DATA_DIR = Path(__file__).resolve().parent / "data"

load_dotenv(ROOT / ".env")

DATABASE = os.getenv("MONGODB_DATABASE", "interview_coach")
VOYAGE_MODEL = os.getenv("VOYAGE_MODEL", "voyage-4-lite")
EMBEDDING_DIMENSIONS = int(os.getenv("VOYAGE_DIMENSIONS", "1024"))
CHAT_TTL_DAYS = int(os.getenv("CHAT_MESSAGES_TTL_DAYS", "30"))

SESSIONS = "sessions"
FEEDBACK_ITEMS = "feedback_items"
USER_PROFILE = "user_profile"
CHAT_MESSAGES = "chat_messages"

VECTOR_INDEX = "feedback_vector_index"


def require(name: str) -> str:
    value = os.getenv(name)
    if not value:
        raise SystemExit(f"Missing {name}. Copy .env.sample to .env and fill it in.")
    return value
