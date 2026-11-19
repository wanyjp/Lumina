namespace Lumina.Domain.Enums;

/// <summary>倒计时分类，默认 Other。</summary>
public enum CountdownCategory
{
    Work = 0,
    Life = 1,
    Exam = 2,
    Anniversary = 3,
    Other = 4,
}

/// <summary>倒计时重复方式。</summary>
public enum CountdownRepeat
{
    None = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>紧迫度五档；对应原型 `normal/mid/near/imminent/expired`。</summary>
public enum UrgencyLevel
{
    Calm = 0,
    Warm = 1,
    Hot = 2,
    Urgent = 3,
    Expired = 4,
}
