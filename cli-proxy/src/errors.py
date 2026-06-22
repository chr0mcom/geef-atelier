"""OpenAI-shaped error envelope and FastAPI exception handlers.

OpenAI clients (incl. the openai-python/-node SDKs and the Geef.Atelier .NET client)
parse errors as {"error": {"message", "type", "param", "code"}} and key their retry
logic on the HTTP status code. FastAPI's defaults ({"detail": ...}, 422 for validation)
do not match, so we reshape every error into this envelope with a faithful status code.
"""
from __future__ import annotations

import logging

from fastapi import Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

log = logging.getLogger(__name__)


class OpenAIError(Exception):
    """Raise to return an OpenAI-shaped error with an explicit status code."""

    def __init__(
        self,
        message: str,
        *,
        status_code: int = 400,
        type: str = "invalid_request_error",
        code: str | None = None,
        param: str | None = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.status_code = status_code
        self.type = type
        self.code = code
        self.param = param


def error_body(
    message: str,
    *,
    type: str = "invalid_request_error",
    code: str | None = None,
    param: str | None = None,
) -> dict:
    """Build the OpenAI error envelope body."""
    return {"error": {"message": message, "type": type, "code": code, "param": param}}


def error_response(
    message: str,
    *,
    status_code: int = 400,
    type: str = "invalid_request_error",
    code: str | None = None,
    param: str | None = None,
) -> JSONResponse:
    return JSONResponse(
        status_code=status_code,
        content=error_body(message, type=type, code=code, param=param),
    )


def _type_for_status(status_code: int) -> str:
    if status_code == 401:
        return "authentication_error"
    if status_code == 403:
        return "permission_error"
    if status_code == 404:
        return "not_found_error"
    if status_code == 429:
        return "rate_limit_error"
    if 400 <= status_code < 500:
        return "invalid_request_error"
    return "api_error"


async def _openai_error_handler(request: Request, exc: OpenAIError) -> JSONResponse:
    return error_response(
        exc.message, status_code=exc.status_code, type=exc.type, code=exc.code, param=exc.param
    )


async def _validation_handler(request: Request, exc: RequestValidationError) -> JSONResponse:
    # Map FastAPI's 422 to OpenAI's 400 invalid_request_error. Surface the first
    # offending field as `param` for client diagnostics.
    errors = exc.errors()
    first = errors[0] if errors else {}
    loc = [str(p) for p in first.get("loc", []) if p not in ("body",)]
    param = ".".join(loc) if loc else None
    msg = first.get("msg", "Invalid request")
    detail = f"{msg} (param: {param})" if param else msg
    log.info("Validation error: %s", jsonable_encoder(errors))
    return error_response(detail, status_code=400, type="invalid_request_error", param=param)


async def _http_exception_handler(request: Request, exc: StarletteHTTPException) -> JSONResponse:
    # Reshape FastAPI/Starlette HTTPException ({"detail": ...}) into the OpenAI envelope,
    # preserving any headers (e.g. Retry-After / WWW-Authenticate) the raiser attached.
    detail = exc.detail if isinstance(exc.detail, str) else str(exc.detail)
    return JSONResponse(
        status_code=exc.status_code,
        content=error_body(detail, type=_type_for_status(exc.status_code)),
        headers=getattr(exc, "headers", None),
    )


async def _unhandled_handler(request: Request, exc: Exception) -> JSONResponse:
    log.exception("Unhandled error: %s", exc)
    return error_response(
        f"Internal server error: {exc}", status_code=500, type="api_error"
    )


def install_error_handlers(app) -> None:
    """Register the OpenAI-shaped exception handlers on the FastAPI app."""
    app.add_exception_handler(OpenAIError, _openai_error_handler)
    app.add_exception_handler(RequestValidationError, _validation_handler)
    app.add_exception_handler(StarletteHTTPException, _http_exception_handler)
    app.add_exception_handler(Exception, _unhandled_handler)
