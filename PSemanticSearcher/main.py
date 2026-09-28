"""
PSemanticSearcher/main.py

Fixes over the original Semanticsercher.py:
 - actual FastAPI app + /analyze endpoint (the original had no app object)
 - fixed typo `hhtpx` -> `httpx`, `inline_stle` -> `inline_style`
 - embedder is now loaded once at module import, not re-created every loop iteration
 - SemanticSearch.__init__ no longer references self.text/self.query before they exist
 - chunk_text no longer nests the entire search logic inside the chunking loop
 - returns TWO chunks as requested: a general "site info" chunk and a
   "contact info" chunk (the passage most likely to hold phone/email/address)
"""

from typing import Optional

import numpy as np
import faiss
from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer
from lxml.html.clean import Cleaner
import lxml.html as LH
from langdetect import detect, LangDetectException

app = FastAPI()

_model = SentenceTransformer("sentence-transformers/paraphrase-multilingual-mpnet-base-v2")

_html_cleaner = Cleaner(
    scripts=True,
    javascript=True,
    style=True,
    inline_style=True,
    safe_attrs_only=False,
)

SITE_INFO_QUERY = "درباره ما، معرفی شرکت، خدمات و محصولات"
CONTACT_INFO_QUERY = "تلفن تماس، ایمیل، آدرس، ارتباط با ما"

CONTACT_KEYWORDS = ["تماس", "تلفن", "ایمیل", "آدرس", "phone", "email", "contact", "address"]


class AnalyzeRequest(BaseModel):
    html: str


class AnalyzeResponse(BaseModel):
    site_info_chunk: Optional[str] = None
    contact_info_chunk: Optional[str] = None
    language: Optional[str] = None


def strip_noise(html: str) -> str:
    if not html or not html.strip():
        return ""
    try:
        doc = LH.fromstring(html)
        cleaned = _html_cleaner.clean_html(doc)
        return LH.tostring(cleaned, encoding="unicode", method="text")
    except Exception:
        return ""


def chunk_text(text: str, size: int = 200, overlap: int = 40) -> list[str]:
    words = text.split()
    if not words:
        return []
    step = max(size - overlap, 1)
    chunks = []
    for i in range(0, len(words), step):
        chunk = " ".join(words[i:i + size])
        if chunk:
            chunks.append(chunk)
    return chunks


def best_chunk_for_query(chunks: list[str], embeddings: np.ndarray, query: str, threshold: float = 0.35) -> Optional[str]:
    if not chunks:
        return None

    query_vec = _model.encode([query], convert_to_numpy=True).astype("float32")
    faiss.normalize_L2(query_vec)

    dim = embeddings.shape[1]
    index = faiss.IndexFlatIP(dim)
    index.add(embeddings)

    score, indice = index.search(query_vec, 1)
    if score[0][0] >= threshold:
        return chunks[int(indice[0][0])]
    return None


def keyword_fallback_chunk(chunks: list[str]) -> Optional[str]:
    for chunk in chunks:
        lower = chunk.lower()
        if any(kw in lower or kw in chunk for kw in CONTACT_KEYWORDS):
            return chunk
    return None


@app.post("/analyze", response_model=AnalyzeResponse)
def analyze(req: AnalyzeRequest) -> AnalyzeResponse:
    cleaned_text = strip_noise(req.html)
    if not cleaned_text:
        return AnalyzeResponse()

    try:
        language = detect(cleaned_text)
    except LangDetectException:
        language = None

    chunks = chunk_text(cleaned_text)
    if not chunks:
        return AnalyzeResponse(language=language)

    embeddings = _model.encode(chunks, convert_to_numpy=True, show_progress_bar=False).astype("float32")
    faiss.normalize_L2(embeddings)

    site_info_chunk = best_chunk_for_query(chunks, embeddings, SITE_INFO_QUERY) or chunks[0]
    contact_info_chunk = (
        best_chunk_for_query(chunks, embeddings, CONTACT_INFO_QUERY)
        or keyword_fallback_chunk(chunks)
    )

    return AnalyzeResponse(
        site_info_chunk=site_info_chunk,
        contact_info_chunk=contact_info_chunk,
        language=language,
    )

# run with: uvicorn main:app --reload --port 8100
