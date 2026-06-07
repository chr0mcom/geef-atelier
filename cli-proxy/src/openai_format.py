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
    role: Literal["system", "user", "assistant", "tool"]
    content: str | None = None
    tool_call_id: str | None = None
    name: str | None = None


class ChatCompletionRequest(BaseModel):
    model: str
    messages: list[ChatMessage]
    tools: list[ToolDefinition] | None = None
    tool_choice: str | ToolChoiceObject | None = None
    max_tokens: int | None = None
    temperature: float | None = None
    stream: bool = False
    # Document-Mode — opt-in, set only for CLI-provider executor calls.
    # The CLI agent receives the document as draft.md in an ephemeral workspace
    # and edits it in place instead of re-emitting the full text on stdout.
    document_mode: bool = False
    document: str | None = None
    # Large, static background context (grounding + advisor notes) kept separate from the
    # steering instruction. Offloaded to context.md when it exceeds CONTEXT_FILE_THRESHOLD,
    # otherwise prepended inline. Avoids the per-argument argv limit (MAX_ARG_STRLEN, 128 KB).
    context_document: str | None = None


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


class Choice(BaseModel):
    index: int = 0
    message: ChoiceMessage
    finish_reason: Literal["stop", "tool_calls", "length"] = "stop"


class UsageInfo(BaseModel):
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0


class ChatCompletionResponse(BaseModel):
    id: str = Field(default_factory=lambda: f"chatcmpl-{uuid.uuid4().hex}")
    object: Literal["chat.completion"] = "chat.completion"
    created: int = Field(default_factory=lambda: int(time.time()))
    model: str
    choices: list[Choice]
    usage: UsageInfo = Field(default_factory=UsageInfo)


def make_text_response(model: str, text: str) -> ChatCompletionResponse:
    """Wrap plain text output as a non-tool ChatCompletionResponse."""
    return ChatCompletionResponse(
        model=model,
        choices=[Choice(message=ChoiceMessage(content=text), finish_reason="stop")],
    )


def make_tool_response(model: str, tool_name: str, arguments_json: str) -> ChatCompletionResponse:
    """Wrap a tool call result as a ChatCompletionResponse with finish_reason='tool_calls'."""
    return ChatCompletionResponse(
        model=model,
        choices=[
            Choice(
                message=ChoiceMessage(
                    tool_calls=[ToolCall(function=FunctionCall(name=tool_name, arguments=arguments_json))]
                ),
                finish_reason="tool_calls",
            )
        ],
    )
