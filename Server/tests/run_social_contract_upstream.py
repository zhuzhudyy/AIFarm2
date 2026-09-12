"""Controlled test upstream for Unity activity/social integration, never a real model."""

import json
from collections import Counter

import uvicorn
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.providers import MockProvider, ProviderInputError
from app.schemas import GenerateUtteranceRequest, ResidentTaskRequest


app = FastAPI(title="AIFarm controlled social test upstream (NOT a real model)")
events = []
decisions = Counter()
control = {"fail_requests": False, "enable_social": True}


@app.get("/contract-evidence")
def evidence():
    return {"test_only": True, "real_model": False, "events": events, "control": control}


@app.post("/contract-control")
async def configure_test(request: Request):
    values = await request.json()
    for key in control:
        if key in values and isinstance(values[key], bool):
            control[key] = values[key]
    return control


@app.post("/v1/chat/completions")
async def completion(request: Request):
    if control["fail_requests"]:
        return JSONResponse({"error": {"message": "Controlled upstream outage for recovery verification."}}, status_code=503)
    body = await request.json()
    text = body["messages"][-1]["content"]
    try:
        snapshot = json.loads(text.split("\nPrevious output")[0])
    except ValueError:
        snapshot = {}
    owner = snapshot.get("resident_id", "probe")
    event = {"resident_id": owner, "model": body.get("model"), "test_only": True}
    if "allowed_intents" in snapshot:
        decisions[owner] += 1
        allowed = snapshot["allowed_intents"]
        targets = snapshot.get("allowed_target_resident_ids", [])
        choose_social = control["enable_social"] and bool(targets) and "RequestConversation" in allowed and decisions[owner] % 3 == 1
        intent = "RequestConversation" if choose_social else allowed[0]
        target = targets[0] if choose_social else None
        payload = {"resident_id": owner, "intent": intent, "target_resident_id": target,
            "reason": "受控模型选择一项当前允许的交流活动。" if choose_social else "受控模型选择当前可执行的活动。"}
        event.update(operation="resident_decision", intent=intent, target_resident_id=target, allowed_intents=allowed, allowed_targets=targets)
    elif "command" in snapshot:
        try:
            task = MockProvider().interpret_task(ResidentTaskRequest.model_validate(snapshot))
            payload = task.model_dump(mode="json")
        except ProviderInputError as error:
            payload = {"unsupported": error.code, "message": error.message}
        event.update(operation="resident_task", command=snapshot["command"], task_type=payload.get("task_type"), target_resident_id=payload.get("target_resident_id"))
    elif "participants" in snapshot:
        participants = snapshot["participants"]
        topic = snapshot.get("topic", "今天的工作")[:80]
        payload = {"resident_id": owner, "outcome": "Helpful", "lines": [
            {"speaker_id": participant["resident_id"], "mood": "Happy", "emoji": "🙂", "shared_knowledge_id": None,
             "text": f"[受控模型对话] 我是{participant['persona']['display_name']}。刚才在忙{participant.get('current_state', '')[:90]}。我们聊聊{topic}。"[:280]}
            for participant in participants]}
        event.update(operation="conversation", participants=[participant["resident_id"] for participant in participants], line_count=len(payload["lines"]))
    elif "trigger" in snapshot:
        payload = MockProvider().generate_utterance(GenerateUtteranceRequest.model_validate(snapshot)).model_dump(mode="json")
        event.update(operation="utterance", trigger=snapshot["trigger"])
    elif "outcome" in snapshot:
        payload = {"resident_id": owner, "outcome": snapshot["outcome"], "mood": "Focused", "emoji": "🙂", "text": "[受控模型反思] " + snapshot.get("event_summary", "这项活动已经结束。")[:180]}
        if "goal_id" in snapshot:
            payload.pop("resident_id")
            payload["goal_id"] = snapshot["goal_id"]
        event.update(operation="reflection")
    else:
        payload = None
        event.update(operation="text")
    events.append(event)
    content = json.dumps(payload, ensure_ascii=False) if payload is not None else "受控推理协议检查正常；这不是一个真实模型。"
    return {"id": "social-contract-" + str(len(events)), "choices": [{"message": {"role": "assistant", "content": content}}]}


if __name__ == "__main__":
    print("TEST ONLY; real_model=false; upstream=http://127.0.0.1:8012", flush=True)
    uvicorn.run(app, host="127.0.0.1", port=8012, access_log=False)
