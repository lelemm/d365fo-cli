---
description: Author or extend custom Business Events in D365 Finance & Operations. Invoke when the user asks to "create a business event", "add a business event", "build a custom business event", or wire D365FO outbound notifications to Power Automate / Service Bus / Event Grid.
applyTo: '**/AxClass/**,**/*BusinessEvent*.xml'
---
> ⛔ **NEVER write X++ AOT XML files directly** via PowerShell, terminal file commands (`Set-Content`, `Out-File`, `New-Item`), editor write tools, or any raw text approach. The XML schema is proprietary. **ALWAYS use `d365fo generate …` commands** to produce correct AOT XML. If `d365fo` is unavailable in PATH, stop and ask the user to install it.

# Business Events Authoring in D365FO

A custom business event consists of two classes:
1. **Event class** — extends `BusinessEventsBase`, decorated with `[BusinessEvents(...)]`
2. **Contract class** — implements `BusinessEventsContract`, decorated with `[DataContractAttribute]`

## Pre-flight

```sh
d365fo search business-event <namePart> --output json   # avoid duplication
d365fo get business-event <ExistingName> --output json  # inspect reference pattern
d365fo get table <PrimaryTable> --output json           # confirm table fields
```

## Scaffolding

```sh
d365fo generate business-event CustPaymentBusinessEvent \
  --contract-name CustPaymentBusinessEventContract \
  --payload "custAccount:CustAccount" \
  --payload "paymentAmount:AmountCur" \
  --category "CustomerPayments" \
  --primary-table CustTrans \
  --out c:/AOT/MyModel/AxClass/CustPaymentBusinessEvent.xml \
  --out-contract c:/AOT/MyModel/AxClass/CustPaymentBusinessEventContract.xml
```

## Event class skeleton

```xpp
[BusinessEvents(classStr(CustPaymentBusinessEvent), classStr(CustPaymentBusinessEventContract),
    "CustomerPayments", "@MyModel:Desc")]
public final class CustPaymentBusinessEvent extends BusinessEventsBase
{
    private CustTrans custTrans;
    public static CustPaymentBusinessEvent newFromCustTrans(CustTrans _custTrans)
    {
        var event = new CustPaymentBusinessEvent();
        event.parmCustTrans(_custTrans);
        return event;
    }
    [Wrappable(true), Replaceable(true)]
    public BusinessEventsContract buildContract()
    {
        var c = new CustPaymentBusinessEventContract();
        c.parmCustAccount(custTrans.AccountNum);
        return c;
    }
}
```

## Hard rules

- `[BusinessEvents(...)]` must be on the **class declaration** — four args: event class, contract class, category string, label.
- `buildContract()` must **never return null**.
- Contract `parmXxx()` accessors must have `[DataMemberAttribute]`.
- Use EDTs for payload fields. Run `d365fo get edt <Name>` to confirm.
- Never call `BusinessEventsBase::publish()` inside a `ttsbegin/ttscommit` block unless rollback-publish is intentional.

## Activation

After compile: **System Administration > Business events catalog** → Activate → configure endpoint (Service Bus / Event Grid / Power Automate / HTTP).
