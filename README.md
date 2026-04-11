# FleetManager — VPS Agent & Browser Automation Fleet

A self-hosted browser automation fleet management system. Controls up to **200 accounts** across **4 Linux VPS nodes** (50 per node) with smart proxy rotation, anti-detect Chromium, and real-time noVNC operator takeover.

## Architecture

```
[Desktop WPF App] <──SignalR/REST──> [ASP.NET Core API] <──polling──> [VPS Agent]
                                                                              │
                                                                   [Docker Containers]
                                                                 (Chromium + Playwright
                                                                  + Xvfb + noVNC)
```

---

## Quick Start — Install Agent on Ubuntu 22.04

### 1. Publish Agent
```bash
dotnet publish src/FleetManager.Agent/FleetManager.Agent.csproj \
  -c Release -r linux-x64 --self-contained -o /tmp/fleet-agent-publish
```

### 2. Copy to VPS & Install
```bash
scp -r /tmp/fleet-agent-publish user@VPS_IP:/tmp/fleetmanager-agent
scp -r deploy/linux user@VPS_IP:/tmp/fleet-deploy
ssh user@VPS_IP "sudo bash /tmp/fleet-deploy/install-worker-ubuntu.sh /tmp/fleetmanager-agent"
```

### 3. Register Node
```bash
bash deploy/linux/register-node.sh \
  --api https://your-api.example.com \
  --name VPS-01 --ip 10.0.0.21 \
  --ssh-user fleetmgr --os Ubuntu --region eu-west
```

### 4. Configure `appsettings.json` on VPS
```json
{
  "Agent": {
    "NodeId": "<GUID from register-node.sh>",
    "BackendBaseUrl": "https://your-api.example.com",
    "ApiKey": "<your-secret-api-key>",
    "HeartbeatIntervalSeconds": 15,
    "CommandPollIntervalSeconds": 3,
    "CommandScriptsPath": "/opt/fleetmanager-agent/commands"
  }
}
```

### 5. Build Browser Docker Image (on each VPS)
```bash
docker build -f scripts/Dockerfile.browser -t fleet-browser .
```

---

## Service Management
```bash
sudo systemctl status fleetmanager-agent
sudo journalctl -u fleetmanager-agent -f
sudo systemctl restart fleetmanager-agent
```

---

## Security
- Agent runs as `fleetmgr` system user (least privilege)
- All commands validated against a static allowlist — no arbitrary shell execution
- API communication uses API key (`X-Api-Key` header)
- `forbid_shell_passthrough: true` enforced

---

## Original Skeleton Notes

هذه نسخة **مترابطة ومتناسقة** من مشروع **FleetManager** لإدارة VPS nodes، تثبيت Agent آمن، heartbeat، browser/session orchestration، alerts، وmanual queue.

## ما الذي أصبح مترابطًا فعليًا؟
1. **Infrastructure** تزرع demo seed data مترابطة بين nodes والحسابات ومراحل العمل والتنبيهات.
2. **API** تعرض nodes والحسابات والتفاصيل المرحلية من نفس المصدر.
3. **Desktop** تجلب:
   - `GET /api/nodes`
   - `GET /api/accounts?nodeId=...`
   - `GET /api/accounts/{accountId}/stage-alerts`
4. عند اختيار VPS يتم فلترة الحسابات الخاصة بها.
5. عند النقر على الحساب تظهر المرحلة الحالية والتنبيه النشط وتُلوَّن المرحلة المتأثرة مباشرة.
6. عند غياب الـ API، تتراجع الواجهة تلقائيًا إلى **Demo mode** بنفس السيناريوهات حتى يبقى الـ skeleton قابلًا للعرض.

## Included Projects
- `FleetManager.Api` - ASP.NET Core API + SignalR hub + controllers
- `FleetManager.Application` - use cases, interfaces, application services
- `FleetManager.Domain` - entities, enums, shared domain rules
- `FleetManager.Infrastructure` - EF Core DbContext + repositories + seed data
- `FleetManager.Contracts` - shared request/response DTOs
- `FleetManager.Agent` - .NET Worker Service للـ node agent
- `FleetManager.Desktop` - WPF dashboard مترابطة end-to-end
- `FleetManager.Api.Tests` - مشروع اختبارات مبدئي

## Security Posture
هذا الـ skeleton **لا** يحتوي على:
- root/full access flows
- arbitrary remote shell execution
- open-all firewall defaults
- plaintext secret storage

بل يعتمد على:
- least privilege
- allowlisted commands
- auditable operations
- short-lived enrollment tokens

## Desktop Defaults
- API base URL الافتراضي: `http://localhost:5188/`
- يمكن تغييره عبر environment variable باسم:
  - `FLEETMANAGER_API_BASE_URL`

## Open the Solution
افتح الملف:
- `FleetManager.sln`

## Suggested Next Tasks
1. استبدال InMemory DB بـ PostgreSQL وEF Core migrations
2. تفعيل authentication/authorization
3. ربط الـ Desktop عبر DI بدل الإنشاء المباشر للخدمات
4. إضافة acknowledge/retry/open logs على مستوى التنبيه المرحلي
5. تنفيذ manual queue screen وInstall jobs screen فعليًا
6. بث التغييرات الفعلية عبر SignalR بدل polling/refresh

## Notes
- ملف `docs/` تم تحديثه ليعكس ربط الحسابات والتنبيهات المرحلية.
- مشروع `Desktop` بات يعرض nodes ثم الحسابات ثم تفاصيل المرحلة في لوحة واحدة.
- لم أتمكن من تنفيذ build داخل هذه البيئة لأن `dotnet` غير متوفر هنا.
