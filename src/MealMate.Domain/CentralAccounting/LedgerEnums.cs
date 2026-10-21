namespace MealMate.Domain.CentralAccounting;

public enum AccountingPeriodState { Open = 1, PendingClose = 2, Closed = 3 }
public enum JournalSourceDocumentType { Adjustment = 1, OpeningBalance = 2, Reversal = 3 }
public enum JournalSourceDocumentStatus { Draft = 1, PendingApproval = 2, Approved = 3, Rejected = 4, Posted = 5 }
public enum JournalSourceLinkType { Adjustment = 1, OpeningBalance = 2, Reversal = 3 }
public enum PostingEffect { Original = 1, FullReversal = 2 }
public enum AccountNormalBalance { Debit = 1, Credit = 2 }
