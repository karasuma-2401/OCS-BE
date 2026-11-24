# Incident compensation and consignment settlement

This workflow covers consigned products only. Staff can read every incident report and create reports; managers can review reports and manage payouts. The API enforces permissions independently of the frontend menu.

## Incident workflow

1. Staff looks up a product by barcode, records the incident type and description, uploads one or more evidence photos, and submits the report.
2. Submission is accepted only for a consigned product currently on sale. The product moves to `IncidentPending` and is held from sale while the report is reviewed.
3. A manager either requests recheck with a reason or approves compensation. Recheck returns the report to staff for edits and keeps the product held.
4. The default compensation is the agreed consignment price. A manager may approve a lower amount with a reason; an amount above the agreed price is rejected.
5. Approval records the manager and timestamp, changes the product to `Compensated`, and makes the compensation eligible for a payout. The incident report changes to `Compensated` after that payout is completed.
6. Incident edits and review actions are written to the audit log. Evidence is stored under the private `inspections/` storage area.

Incident states: `Pending`, `NeedsRecheck`, `Approved`, `Compensated`.

## Payout workflow

- Managers can preview and create multiple payout batches for the same contract while products are still selling. A new batch includes only eligible, unpaid product sale lines and approved compensation reports.
- Each sold product appears as separate `SaleProceeds` and negative `Commission` lines. Compensation is a separate positive line. Service fees are not included.
- A batch is blocked while any incident report is pending/recheck, a sold invoice remains unpaid, or a previous batch is not completed.
- Payout state moves `Pending` → `Approved` → `Transferred` → `Completed`. Transfer requires a bank reference and private proof image and creates a successful expense transaction. The customer can confirm receipt; Hangfire completes an unconfirmed transfer after 48 hours.
- Completion links compensation details to their incident reports and changes those reports from `Approved` to `Compensated`.

## Permissions

| Role | Incident permissions | Settlement permissions |
| --- | --- | --- |
| Staff | `incidents.read`, `incidents.create` | No settlement access |
| Manager | `incidents.read`, `incidents.create`, `incidents.review` | `settlements.read`, `settlements.manage` |
| Admin | all incident and settlement permissions | all incident and settlement permissions |

## Endpoints

| Method | Route | Permission / role | Purpose |
| --- | --- | --- | --- |
| GET | `/api/incidents/products/by-barcode?barcode=...` | `incidents.read` | Find a consigned product |
| GET | `/api/incidents` | `incidents.read` | Search and filter all reports |
| GET | `/api/incidents/{id}` | `incidents.read` | Read report, evidence references, and audit history |
| POST | `/api/incidents` | `incidents.create` | Create a report and hold product |
| POST | `/api/incidents/{id}/resubmit` | `incidents.create` | Submit requested corrections |
| POST | `/api/incidents/{id}/approve` | Manager/Admin + `incidents.review` | Approve compensation |
| POST | `/api/incidents/{id}/request-recheck` | Manager/Admin + `incidents.review` | Return report for more evidence |
| GET | `/api/settlements/contracts` | Manager/Admin + `settlements.read` | List contracts for settlement |
| GET | `/api/settlements/contracts/{id}/preview` | Manager/Admin + `settlements.read` | Preview eligible payout lines |
| POST | `/api/settlements/contracts/{id}/payouts` | `settlements.manage` | Create a payout batch |
| GET | `/api/settlements/payouts` | Manager/Admin + `settlements.read` | List payout batches |
| POST | `/api/settlements/payouts/{id}/approve` | `settlements.manage` | Approve a payout batch |
| POST | `/api/settlements/payouts/{id}/transfer` | `settlements.manage` | Record bank transfer and proof |
| GET | `/api/customer/payouts` | Customer | List payouts belonging to the current customer |
| GET | `/api/customer/payouts/{id}/proof` | Owning Customer | Read the private transfer proof |
| POST | `/api/customer/payouts/{id}/confirm-received` | Owning Customer | Confirm receipt and complete the payout |

Apply the generated `IncidentCompensationWorkflow` migration before deployment. Existing accounts need a refreshed login session after migration so their newly seeded role permissions appear in authorization claims.
