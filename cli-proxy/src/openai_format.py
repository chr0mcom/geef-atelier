"""OpenAI Chat Completions API schema models."""
from __future__ import annotations

import time
import uuid
from typing import Any, Literal

from pydantic import BaseModel, Field


class FunctionDefinition(BaseModel):
    name: str
    description: str = ""
    parameters: dict[str, Any] = Field(default_factory=dict)


class ToolDefinition(BaseModel):
    type: Literal["function"] = "function"
    function: FunctionDefinition


class ToolChoiceFunction(BaseModel):
    name: str


class ToolChoiceObject(BaseModel):
    type: Literal["function"] = "function"
    function: ToolChoiceFunction


class ChatMessage(BaseModel):
    # `developer` is the newer alias for `system` (reasoning models).
    role: Literal["system", "developer", "user", "assistant", "tool"]
    # content may be a plain string OR an array of typed parts (text / image_url / …).
    # Kept loosely typed so novel part types never cause a 422; parts are interpreted
    # by message_text()/message_images() below.
    content: str | list[Any] | None = None
    tool_call_id: str | None = None
    name: str | None = None
    # Assistant messages replayed in a multi-turn conversation carry their prior tool_calls.
    tool_calls: list[dict[str, Any]] | None = None
    refusal: str | None = None


class StreamOptions(BaseModel):
    include_usage: bool = False


class JsonSchemaSpec(BaseModel):
    name: str = "response"
    schema_: dict[str, Any] | None = Field(default=None, alias="schema")
    strict: bool | None = None

    model_config = {"populate_by_name": True}


class ResponseFormat(BaseModel):
    type: Literal["text", "json_object", "json_schema"] = "text"
    json_schema: JsonSchemaSpec | None = None


class ChatCompletionRequest(BaseModel):
    model: str
    messages: list[ChatMessage]
    tools: list[ToolDefinition] | None = None
    tool_choice: str | ToolChoiceObject | None = None
    max_tokens: int | None = None
    # max_completion_tokens supersedes the deprecated max_tokens; effective_max_tokens() merges them.
    max_completion_tokens: int | None = None
    temperature: float | None = None
    stream: bool = False
    stream_options: StreamOptions | None = None
    response_format: ResponseFormat | None = None
    # --- Accepted-but-not-honored sampling hints (headless CLIs expose no sampling control).
    top_p: float | None = None
    presence_penalty: float | None = None
    frequency_penalty: float | None = None
    logit_bias: dict[str, float] | None = None
    seed: int | None = None
    stop: str | list[str] | None = None
    # --- Accepted pass-through metadata (no effect on CLI execution).
    parallel_tool_calls: bool | None = None
    service_tier: str | None = None
    user: str | None = None
    metadata: dict[str, Any] | None = None
    store: bool | None = None
    reasoning_effort: str | None = None
    # --- Output features the CLI backend cannot satisfy; rejected in validation (see main._validate_policy).
    n: int | None = None
    logprobs: bool | None = None
    top_logprobs: int | None = None
    # Document-Mode — opt-in, set only for CLI-provider executor calls.
    # The CLI agent receives the document as draft.md in an ephemeral workspace
    # and edits it in place instead of re-emitting the full text on stdout.
    document_mode: bool = False
    document: str | None = None
    # Large, static background context (grounding + advisor notes) kept separate from the
    # steering instruction. Offloaded to context.md when it exceeds CONTEXT_FILE_THRESHOLD,
    # otherwise prepended inline. Avoids the per-argument argv limit (MAX_ARG_STRLEN, 128 KB).
    context_document: str | None = None

    def effective_max_tokens(self) -> int | None:
        return self.max_completion_tokens or self.max_tokens


def message_text(content: str | list[Any] | None) -> str:
    """Flatten message content (string or part array) to plain text for the CLI prompt."""
    if content is None:
        return ""
    if isinstance(content, str):
        return content
    parts: list[str] = []
    for part in content:
        if isinstance(part, str):
            parts.append(part)
        elif isinstance(part, dict):
            if part.get("type") == "text" and part.get("text"):
                parts.append(str(part["text"]))
            elif "text" in part and isinstance(part["text"], str):
                parts.append(part["text"])
    return "\n".join(parts)


def message_images(content: str | list[Any] | None) -> list[dict[str, Any]]:
    """Extract image_url parts from message content (for multimodal handling)."""
    if not isinstance(content, list):
        return []
    images: list[dict[str, Any]] = []
    for part in content:
        if isinstance(part, dict) and part.get("type") == "image_url":
            img = part.get("image_url")
            if isinstance(img, dict) and img.get("url"):
                images.append(img)
    return images


class FunctionCall(BaseModel):
    name: str
    arguments: str


class ToolCall(BaseModel):
    id: str = Field(default_factory=lambda: f"call_{uuid.uuid4().hex[:24]}")
    type: Literal["function"] = "function"
    function: FunctionCall


class ChoiceMessage(BaseModel):
    role: Literal["assistant"] = "assistant"
    content: str | None = None
    tool_calls: list[ToolCall] | None = None
    refusal: str | None = None


class Choice(BaseModel):
    index: int = 0
    message: ChoiceMessage
    finish_reason: Literal["stop", "tool_calls", "length"] = "stop"


class PromptTokensDetails(BaseModel):
    cached_tokens: int = 0


class CompletionTokensDetails(BaseModel):
    reasoning_tokens: int = 0


class UsageInfo(BaseModel):
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    prompt_tokens_details: PromptTokensDetails | None = None
    completion_tokens_details: CompletionTokensDetails | None = None


def usage_info(
    input_tokens: int = 0,
    output_tokens: int = 0,
    cached_tokens: int = 0,
    reasoning_tokens: int = 0,
) -> UsageInfo:
    """Build a UsageInfo, attaching detail blocks only when non-zero (OpenAI convention)."""
    return UsageInfo(
        prompt_tokens=input_tokens,
        completion_tokens=output_tokens,
        total_tokens=input_tokens + output_tokens,
        prompt_tokens_details=PromptTokensDetails(cached_tokens=cached_tokens) if cached_tokens else None,
        completion_tokens_details=(
            CompletionTokensDetails(reasoning_tokens=reasoning_tokens) if reasoning_tokens else None
        ),
    )


class ChatCompletionResponse(BaseModel):
    id: str = Field(default_factory=lambda: f"chatcmpl-{uuid.uuid4().hex}")
    object: Literal["chat.completion"] = "chat.completion"
    created: int = Field(default_factory=lambda: int(time.time()))
    model: str
    choices: list[Choice]
    usage: UsageInfo = Field(default_factory=UsageInfo)


def make_text_response(
    model: str, text: str, usage: UsageInfo | None = None
) -> ChatCompletionResponse:
    """Wrap plain text output as a non-tool ChatCompletionResponse."""
    return ChatCompletionResponse(
        model=model,
        choices=[Choice(message=ChoiceMessage(content=text), finish_reason="stop")],
        usage=usage or UsageInfo(),
    )


def make_refusal_response(
    model: str, refusal: str, usage: UsageInfo | None = None
) -> ChatCompletionResponse:
    """Wrap a refusal (e.g. could not produce valid structured output) per OpenAI convention."""
    return ChatCompletionResponse(
        model=model,
        choices=[Choice(message=ChoiceMessage(content=None, refusal=refusal), finish_reason="stop")],
        usage=usage or UsageInfo(),
    )


def make_tool_response(
    model: str, tool_name: str, arguments_json: str, usage: UsageInfo | None = None
) -> ChatCompletionResponse:
    """Wrap a single tool call as a ChatCompletionResponse with finish_reason='tool_calls'."""
    return make_tool_calls_response(model, [(tool_name, arguments_json)], usage)


def make_tool_calls_response(
    model: str, calls: list[tuple[str, str]], usage: UsageInfo | None = None
) -> ChatCompletionResponse:
    """Wrap one or more tool calls (parallel tool calling) with finish_reason='tool_calls'."""
    tool_calls = [
        ToolCall(function=FunctionCall(name=name, arguments=args)) for name, args in calls
    ]
    return ChatCompletionResponse(
        model=model,
        choices=[
            Choice(message=ChoiceMessage(tool_calls=tool_calls), finish_reason="tool_calls")
        ],
        usage=usage or UsageInfo(),
    )
