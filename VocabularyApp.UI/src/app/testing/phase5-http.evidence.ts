// Generated from actual isolated ASP.NET HTTP responses by ReviewPhase5HttpEvidenceTests.
// Test-only: imported by specs, never by application code. No JWTs or production data.
export const phase5HttpEvidence = {
  "source": "WebApplicationFactory HTTP \u002B isolated SQLite; no production data or JWT values",
  "responses": {
    "summary": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "dueCount": 4,
          "newCount": 4,
          "overdueCount": 0,
          "blockedCount": 0,
          "usableDueCount": 4,
          "totalEnrolledCount": 4,
          "nextReviewAt": null,
          "asOfUtc": "2026-10-13T12:00:00Z"
        }
      }
    },
    "due": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "items": [
            {
              "userWordId": 1,
              "word": "review-word-0",
              "nextReviewAt": "2026-10-13T12:00:00Z",
              "reviewStatus": "new",
              "isUsable": true
            },
            {
              "userWordId": 2,
              "word": "review-word-1",
              "nextReviewAt": "2026-10-13T12:00:00Z",
              "reviewStatus": "new",
              "isUsable": true
            }
          ],
          "nextCursor": "CfDJ8AAAAAAAAAAAAAAAAAAAAAATFzo-V6hlXyGPBGP5g3HAuw3KcgMZcTcDUESxBfUMJng1tjD49Vu4A2UMpQ_L7V-cdGrhy75FpnW-hpVKzLA6edBxcRN51uVL0BWxbdz51aL3JABGrQLkOjPyVRVHUkqP7fQw2IEj975ARkcrcX9z4ddJ-L7RaSmXjh4401bGXMM",
          "asOfUtc": "2026-10-13T12:00:00Z"
        }
      }
    },
    "ready": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "ready",
          "reason": null,
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "expiresAtUtc": "2026-10-13T12:30:00Z",
          "items": [
            {
              "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
              "userWordId": 1,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-0\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 2"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 0"
                }
              ],
              "acceptedReceipt": null
            },
            {
              "itemId": "69cf2c4f-4393-4981-8a92-6a016f0fa4d0",
              "userWordId": 2,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-1\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 0"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 2"
                }
              ],
              "acceptedReceipt": null
            }
          ],
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 0,
            "correctCount": 0,
            "incorrectCount": 0,
            "pendingCount": 2,
            "isComplete": false
          }
        }
      }
    },
    "receipt": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
          "submissionId": "5f1d38d8-b0c9-4ec6-b264-c5f6a1b83934",
          "reviewedAtUtc": "2026-10-13T12:00:00Z",
          "isCorrect": false,
          "selectedOptionId": 0,
          "correctOptionId": 3,
          "selectedAnswer": "Meaning number 1",
          "correctAnswer": "Meaning number 0",
          "nextReviewAt": "2026-10-14T12:00:00Z",
          "reviewIntervalDays": 1,
          "reviewStatus": "learning",
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 1,
            "correctCount": 0,
            "incorrectCount": 1,
            "pendingCount": 1,
            "isComplete": false
          }
        }
      }
    },
    "replay": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
          "submissionId": "5f1d38d8-b0c9-4ec6-b264-c5f6a1b83934",
          "reviewedAtUtc": "2026-10-13T12:00:00Z",
          "isCorrect": false,
          "selectedOptionId": 0,
          "correctOptionId": 3,
          "selectedAnswer": "Meaning number 1",
          "correctAnswer": "Meaning number 0",
          "nextReviewAt": "2026-10-14T12:00:00Z",
          "reviewIntervalDays": 1,
          "reviewStatus": "learning",
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 1,
            "correctCount": 0,
            "incorrectCount": 1,
            "pendingCount": 1,
            "isComplete": false
          }
        }
      }
    },
    "resume": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "ready",
          "reason": null,
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "expiresAtUtc": "2026-10-13T12:30:00Z",
          "items": [
            {
              "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
              "userWordId": 1,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-0\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 2"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 0"
                }
              ],
              "acceptedReceipt": {
                "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
                "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
                "submissionId": "5f1d38d8-b0c9-4ec6-b264-c5f6a1b83934",
                "reviewedAtUtc": "2026-10-13T12:00:00Z",
                "isCorrect": false,
                "selectedOptionId": 0,
                "correctOptionId": 3,
                "selectedAnswer": "Meaning number 1",
                "correctAnswer": "Meaning number 0",
                "nextReviewAt": "2026-10-14T12:00:00Z",
                "reviewIntervalDays": 1,
                "reviewStatus": "learning",
                "sessionSummary": {
                  "totalItems": 2,
                  "acceptedCount": 1,
                  "correctCount": 0,
                  "incorrectCount": 1,
                  "pendingCount": 1,
                  "isComplete": false
                }
              }
            },
            {
              "itemId": "69cf2c4f-4393-4981-8a92-6a016f0fa4d0",
              "userWordId": 2,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-1\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 0"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 2"
                }
              ],
              "acceptedReceipt": null
            }
          ],
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 1,
            "correctCount": 0,
            "incorrectCount": 1,
            "pendingCount": 1,
            "isComplete": false
          }
        }
      }
    },
    "stateChanged": {
      "status": 409,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "Your review content or schedule changed. Please start a new review.",
        "message": "Your review content or schedule changed. Please start a new review.",
        "code": "review_state_changed",
        "traceId": "0HNP9HFG7D7VU"
      }
    },
    "conflict": {
      "status": 409,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "This submission key or review item has already been used for a different answer.",
        "message": "This submission key or review item has already been used for a different answer.",
        "code": "review_submission_conflict",
        "traceId": "0HNP9HFG7D7VV"
      }
    },
    "validation": {
      "status": 400,
      "contentType": "application/problem\u002Bjson",
      "body": {
        "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        "title": "One or more validation errors occurred.",
        "status": 400,
        "errors": {
          "$": [
            "Invalid value."
          ],
          "request": [
            "The request field is required."
          ]
        },
        "traceId": "0HNP9HFG7D800",
        "success": false,
        "data": null,
        "error": "One or more request fields are invalid.",
        "message": "One or more request fields are invalid.",
        "code": "validation_failed"
      }
    },
    "missing": {
      "status": 404,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "This review session is unavailable. Please start a new review.",
        "message": "This review session is unavailable. Please start a new review.",
        "code": "review_session_unavailable",
        "traceId": "0HNP9HFG7D801"
      }
    },
    "completedReceipt": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "itemId": "69cf2c4f-4393-4981-8a92-6a016f0fa4d0",
          "submissionId": "68591cb0-5d56-478d-8b8b-6664a440b39d",
          "reviewedAtUtc": "2026-10-13T12:00:00Z",
          "isCorrect": true,
          "selectedOptionId": 0,
          "correctOptionId": 0,
          "selectedAnswer": "Meaning number 1",
          "correctAnswer": "Meaning number 1",
          "nextReviewAt": "2026-10-14T12:00:00Z",
          "reviewIntervalDays": 1,
          "reviewStatus": "learning",
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 2,
            "correctCount": 1,
            "incorrectCount": 1,
            "pendingCount": 0,
            "isComplete": true
          }
        }
      }
    },
    "completed": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "ready",
          "reason": null,
          "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
          "expiresAtUtc": "2026-10-13T12:30:00Z",
          "items": [
            {
              "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
              "userWordId": 1,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-0\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 2"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 0"
                }
              ],
              "acceptedReceipt": {
                "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
                "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
                "submissionId": "5f1d38d8-b0c9-4ec6-b264-c5f6a1b83934",
                "reviewedAtUtc": "2026-10-13T12:00:00Z",
                "isCorrect": false,
                "selectedOptionId": 0,
                "correctOptionId": 3,
                "selectedAnswer": "Meaning number 1",
                "correctAnswer": "Meaning number 0",
                "nextReviewAt": "2026-10-14T12:00:00Z",
                "reviewIntervalDays": 1,
                "reviewStatus": "learning",
                "sessionSummary": {
                  "totalItems": 2,
                  "acceptedCount": 1,
                  "correctCount": 0,
                  "incorrectCount": 1,
                  "pendingCount": 1,
                  "isComplete": false
                }
              }
            },
            {
              "itemId": "69cf2c4f-4393-4981-8a92-6a016f0fa4d0",
              "userWordId": 2,
              "questionType": "word-to-definition",
              "prompt": "What is the meaning of \u0027review-word-1\u0027?",
              "options": [
                {
                  "optionId": 0,
                  "text": "Meaning number 1"
                },
                {
                  "optionId": 1,
                  "text": "Meaning number 3"
                },
                {
                  "optionId": 2,
                  "text": "Meaning number 0"
                },
                {
                  "optionId": 3,
                  "text": "Meaning number 2"
                }
              ],
              "acceptedReceipt": {
                "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
                "itemId": "69cf2c4f-4393-4981-8a92-6a016f0fa4d0",
                "submissionId": "68591cb0-5d56-478d-8b8b-6664a440b39d",
                "reviewedAtUtc": "2026-10-13T12:00:00Z",
                "isCorrect": true,
                "selectedOptionId": 0,
                "correctOptionId": 0,
                "selectedAnswer": "Meaning number 1",
                "correctAnswer": "Meaning number 1",
                "nextReviewAt": "2026-10-14T12:00:00Z",
                "reviewIntervalDays": 1,
                "reviewStatus": "learning",
                "sessionSummary": {
                  "totalItems": 2,
                  "acceptedCount": 2,
                  "correctCount": 1,
                  "incorrectCount": 1,
                  "pendingCount": 0,
                  "isComplete": true
                }
              }
            }
          ],
          "sessionSummary": {
            "totalItems": 2,
            "acceptedCount": 2,
            "correctCount": 1,
            "incorrectCount": 1,
            "pendingCount": 0,
            "isComplete": true
          }
        }
      }
    },
    "expired": {
      "status": 409,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "This review session has expired. Please start a new review.",
        "message": "This review session has expired. Please start a new review.",
        "code": "review_session_expired",
        "traceId": "0HNP9HFG7D802"
      }
    },
    "internal": {
      "status": 500,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "An internal error occurred. Please try again.",
        "message": "An internal error occurred. Please try again.",
        "code": "internal_error",
        "traceId": "0HNP9HFG7D803"
      }
    },
    "unauthorized": {
      "status": 401,
      "contentType": null,
      "body": null
    },
    "disabled": {
      "status": 503,
      "contentType": "application/json",
      "body": {
        "success": false,
        "data": null,
        "error": "Reviews are temporarily unavailable. Please try again later.",
        "message": "Reviews are temporarily unavailable. Please try again later.",
        "code": "reviews_disabled",
        "traceId": "0HNP9HFG7D804"
      }
    },
    "empty": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "empty",
          "reason": null,
          "sessionId": null,
          "expiresAtUtc": null,
          "items": [],
          "sessionSummary": null
        }
      }
    },
    "insufficient": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "unavailable",
          "reason": "insufficient_usable_vocabulary",
          "sessionId": null,
          "expiresAtUtc": null,
          "items": [],
          "sessionSummary": null
        }
      }
    },
    "blocked": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "unavailable",
          "reason": "no_usable_due_words",
          "sessionId": null,
          "expiresAtUtc": null,
          "items": [],
          "sessionSummary": null
        }
      }
    },
    "blockedDue": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "items": [
            {
              "userWordId": 1,
              "word": "blocked-evidence",
              "nextReviewAt": "2026-10-13T12:00:00Z",
              "reviewStatus": "new",
              "isUsable": false
            }
          ],
          "nextCursor": null,
          "asOfUtc": "2026-10-13T12:00:00Z"
        }
      }
    },
    "ambiguous": {
      "status": 200,
      "contentType": "application/json",
      "body": {
        "success": true,
        "data": {
          "availability": "unavailable",
          "reason": "ambiguous_options",
          "sessionId": null,
          "expiresAtUtc": null,
          "items": [],
          "sessionSummary": null
        }
      }
    }
  },
  "submission": {
    "sessionId": "01e8c92f-0126-410e-8e31-fdf6d9a015e3",
    "itemId": "375f88f7-dfce-4819-80d1-37481cac6d2b",
    "submissionId": "5f1d38d8-b0c9-4ec6-b264-c5f6a1b83934",
    "selectedOptionId": 0
  },
  "schemas": {
    "ApiErrorResponse": {
      "required": [
        "code",
        "data",
        "error",
        "message",
        "success",
        "traceId"
      ],
      "type": "object",
      "properties": {
        "success": {
          "enum": [
            false
          ],
          "type": "boolean",
          "readOnly": true
        },
        "data": {
          "enum": [
            null
          ],
          "nullable": true,
          "readOnly": true
        },
        "error": {
          "type": "string"
        },
        "message": {
          "type": "string",
          "description": "Same safe public text as error.",
          "readOnly": true
        },
        "code": {
          "type": "string"
        },
        "traceId": {
          "type": "string",
          "description": "Server HttpContext.TraceIdentifier; caller trace headers are not echoed."
        }
      },
      "additionalProperties": false
    },
    "ReviewDueItemDto": {
      "required": [
        "isUsable",
        "nextReviewAt",
        "reviewStatus",
        "userWordId",
        "word"
      ],
      "type": "object",
      "properties": {
        "userWordId": {
          "type": "integer",
          "format": "int32"
        },
        "word": {
          "type": "string"
        },
        "nextReviewAt": {
          "type": "string",
          "format": "date-time"
        },
        "reviewStatus": {
          "pattern": "^(new|learning|reviewing)$",
          "type": "string"
        },
        "isUsable": {
          "type": "boolean"
        }
      },
      "additionalProperties": false
    },
    "ReviewDueResponseDto": {
      "required": [
        "asOfUtc",
        "items",
        "nextCursor"
      ],
      "type": "object",
      "properties": {
        "items": {
          "type": "array",
          "items": {
            "$ref": "#/components/schemas/ReviewDueItemDto"
          }
        },
        "nextCursor": {
          "type": "string",
          "nullable": true
        },
        "asOfUtc": {
          "type": "string",
          "format": "date-time"
        }
      },
      "additionalProperties": false
    },
    "ReviewDueResponseDtoSuccessResponse": {
      "required": [
        "data",
        "success"
      ],
      "type": "object",
      "properties": {
        "success": {
          "type": "boolean",
          "readOnly": true
        },
        "data": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewDueResponseDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewItemDto": {
      "required": [
        "acceptedReceipt",
        "itemId",
        "options",
        "prompt",
        "questionType",
        "userWordId"
      ],
      "type": "object",
      "properties": {
        "itemId": {
          "type": "string",
          "format": "uuid"
        },
        "userWordId": {
          "type": "integer",
          "format": "int32"
        },
        "questionType": {
          "pattern": "^word-to-definition$",
          "type": "string"
        },
        "prompt": {
          "type": "string"
        },
        "options": {
          "type": "array",
          "items": {
            "$ref": "#/components/schemas/ReviewOptionDto"
          }
        },
        "acceptedReceipt": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewSubmitResponseDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewOptionDto": {
      "required": [
        "optionId",
        "text"
      ],
      "type": "object",
      "properties": {
        "optionId": {
          "maximum": 3,
          "minimum": 0,
          "type": "integer",
          "format": "int32"
        },
        "text": {
          "type": "string"
        }
      },
      "additionalProperties": false
    },
    "ReviewSessionSummaryDto": {
      "required": [
        "acceptedCount",
        "correctCount",
        "incorrectCount",
        "isComplete",
        "pendingCount",
        "totalItems"
      ],
      "type": "object",
      "properties": {
        "totalItems": {
          "type": "integer",
          "format": "int32"
        },
        "acceptedCount": {
          "type": "integer",
          "format": "int32"
        },
        "correctCount": {
          "type": "integer",
          "format": "int32"
        },
        "incorrectCount": {
          "type": "integer",
          "format": "int32"
        },
        "pendingCount": {
          "type": "integer",
          "format": "int32"
        },
        "isComplete": {
          "type": "boolean"
        }
      },
      "additionalProperties": false
    },
    "ReviewStartRequestDto": {
      "type": "object",
      "properties": {
        "questionCount": {
          "maximum": 20,
          "minimum": 1,
          "type": "integer",
          "format": "int32",
          "default": 10
        },
        "resumeSessionId": {
          "type": "string",
          "format": "uuid",
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewStartResponseDto": {
      "required": [
        "availability",
        "expiresAtUtc",
        "items",
        "reason",
        "sessionId",
        "sessionSummary"
      ],
      "type": "object",
      "properties": {
        "availability": {
          "pattern": "^(ready|empty|unavailable)$",
          "type": "string"
        },
        "reason": {
          "pattern": "^(insufficient_usable_vocabulary|ambiguous_options|no_usable_due_words)$",
          "type": "string",
          "nullable": true
        },
        "sessionId": {
          "type": "string",
          "format": "uuid",
          "nullable": true
        },
        "expiresAtUtc": {
          "type": "string",
          "format": "date-time",
          "nullable": true
        },
        "items": {
          "type": "array",
          "items": {
            "$ref": "#/components/schemas/ReviewItemDto"
          }
        },
        "sessionSummary": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewSessionSummaryDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewStartResponseDtoSuccessResponse": {
      "required": [
        "data",
        "success"
      ],
      "type": "object",
      "properties": {
        "success": {
          "type": "boolean",
          "readOnly": true
        },
        "data": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewStartResponseDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewSubmitRequestDto": {
      "required": [
        "itemId",
        "selectedOptionId",
        "sessionId",
        "submissionId"
      ],
      "type": "object",
      "properties": {
        "sessionId": {
          "type": "string",
          "format": "uuid"
        },
        "itemId": {
          "type": "string",
          "format": "uuid"
        },
        "submissionId": {
          "type": "string",
          "format": "uuid"
        },
        "selectedOptionId": {
          "maximum": 3,
          "minimum": 0,
          "type": "integer",
          "format": "int32"
        }
      },
      "additionalProperties": false
    },
    "ReviewSubmitResponseDto": {
      "required": [
        "correctAnswer",
        "correctOptionId",
        "isCorrect",
        "itemId",
        "nextReviewAt",
        "reviewedAtUtc",
        "reviewIntervalDays",
        "reviewStatus",
        "selectedAnswer",
        "selectedOptionId",
        "sessionId",
        "sessionSummary",
        "submissionId"
      ],
      "type": "object",
      "properties": {
        "sessionId": {
          "type": "string",
          "format": "uuid"
        },
        "itemId": {
          "type": "string",
          "format": "uuid"
        },
        "submissionId": {
          "type": "string",
          "format": "uuid"
        },
        "reviewedAtUtc": {
          "type": "string",
          "format": "date-time"
        },
        "isCorrect": {
          "type": "boolean"
        },
        "selectedOptionId": {
          "type": "integer",
          "format": "int32"
        },
        "correctOptionId": {
          "type": "integer",
          "format": "int32"
        },
        "selectedAnswer": {
          "type": "string"
        },
        "correctAnswer": {
          "type": "string"
        },
        "nextReviewAt": {
          "type": "string",
          "format": "date-time"
        },
        "reviewIntervalDays": {
          "type": "integer",
          "format": "int32"
        },
        "reviewStatus": {
          "pattern": "^(learning|reviewing)$",
          "type": "string"
        },
        "sessionSummary": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewSessionSummaryDto"
            }
          ]
        }
      },
      "additionalProperties": false
    },
    "ReviewSubmitResponseDtoSuccessResponse": {
      "required": [
        "data",
        "success"
      ],
      "type": "object",
      "properties": {
        "success": {
          "type": "boolean",
          "readOnly": true
        },
        "data": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewSubmitResponseDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    },
    "ReviewSummaryResponseDto": {
      "required": [
        "asOfUtc",
        "blockedCount",
        "dueCount",
        "newCount",
        "nextReviewAt",
        "overdueCount",
        "totalEnrolledCount",
        "usableDueCount"
      ],
      "type": "object",
      "properties": {
        "dueCount": {
          "type": "integer",
          "format": "int32"
        },
        "newCount": {
          "type": "integer",
          "format": "int32"
        },
        "overdueCount": {
          "type": "integer",
          "format": "int32"
        },
        "blockedCount": {
          "type": "integer",
          "format": "int32"
        },
        "usableDueCount": {
          "type": "integer",
          "format": "int32"
        },
        "totalEnrolledCount": {
          "type": "integer",
          "format": "int32"
        },
        "nextReviewAt": {
          "type": "string",
          "format": "date-time",
          "nullable": true
        },
        "asOfUtc": {
          "type": "string",
          "format": "date-time"
        }
      },
      "additionalProperties": false
    },
    "ReviewSummaryResponseDtoSuccessResponse": {
      "required": [
        "data",
        "success"
      ],
      "type": "object",
      "properties": {
        "success": {
          "type": "boolean",
          "readOnly": true
        },
        "data": {
          "allOf": [
            {
              "$ref": "#/components/schemas/ReviewSummaryResponseDto"
            }
          ],
          "nullable": true
        }
      },
      "additionalProperties": false
    }
  },
  "paths": {
    "/api/reviews/due": {
      "get": {
        "tags": [
          "Reviews"
        ],
        "description": " Stable application errors: 400: validation_failed; 401: invalid_token; 503: reviews_disabled; 500: internal_error.",
        "parameters": [
          {
            "name": "Take",
            "in": "query",
            "schema": {
              "maximum": 100,
              "minimum": 1,
              "type": "integer",
              "format": "int32",
              "default": 20
            }
          },
          {
            "name": "Cursor",
            "in": "query",
            "schema": {
              "maxLength": 2048,
              "minLength": 0,
              "type": "string"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "OK",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ReviewDueResponseDtoSuccessResponse"
                }
              }
            }
          },
          "400": {
            "description": "Binding/validation failure: extended ValidationProblemDetails, code validation_failed.",
            "content": {
              "application/problem\u002Bjson": {
                "schema": {
                  "required": [
                    "type",
                    "title",
                    "status",
                    "errors",
                    "traceId",
                    "success",
                    "data",
                    "error",
                    "message",
                    "code"
                  ],
                  "type": "object",
                  "properties": {
                    "type": {
                      "type": "string"
                    },
                    "title": {
                      "type": "string"
                    },
                    "status": {
                      "enum": [
                        400
                      ],
                      "type": "integer"
                    },
                    "detail": {
                      "type": "string",
                      "nullable": true
                    },
                    "instance": {
                      "type": "string",
                      "nullable": true
                    },
                    "errors": {
                      "type": "object",
                      "additionalProperties": {
                        "type": "array",
                        "items": {
                          "type": "string"
                        }
                      }
                    },
                    "traceId": {
                      "type": "string"
                    },
                    "success": {
                      "enum": [
                        false
                      ],
                      "type": "boolean"
                    },
                    "data": {
                      "enum": [
                        null
                      ],
                      "type": "object",
                      "nullable": true
                    },
                    "error": {
                      "type": "string"
                    },
                    "message": {
                      "type": "string"
                    },
                    "code": {
                      "enum": [
                        "validation_failed"
                      ],
                      "type": "string"
                    }
                  },
                  "description": "ValidationProblemDetails with sanitized field errors and R7 compatibility extensions. Optional standard ProblemDetails members may also occur."
                }
              }
            }
          },
          "401": {
            "description": "Framework Bearer challenge may have an empty body. If the action rejects an authenticated principal, application/json ApiErrorResponse is returned; see the documented codes.",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "503": {
            "description": "Service Unavailable Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "500": {
            "description": "Internal Server Error Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          }
        },
        "security": [
          {
            "Bearer": []
          }
        ]
      }
    },
    "/api/reviews/summary": {
      "get": {
        "tags": [
          "Reviews"
        ],
        "description": " Stable application errors: 401: invalid_token; 503: reviews_disabled; 500: internal_error.",
        "responses": {
          "200": {
            "description": "OK",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ReviewSummaryResponseDtoSuccessResponse"
                }
              }
            }
          },
          "401": {
            "description": "Framework Bearer challenge may have an empty body. If the action rejects an authenticated principal, application/json ApiErrorResponse is returned; see the documented codes.",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "503": {
            "description": "Service Unavailable Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "500": {
            "description": "Internal Server Error Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          }
        },
        "security": [
          {
            "Bearer": []
          }
        ]
      }
    },
    "/api/reviews/start": {
      "post": {
        "tags": [
          "Reviews"
        ],
        "description": " Stable application errors: 400: validation_failed; 401: invalid_token; 404: review_session_unavailable; 409: review_session_expired, review_state_changed; 503: reviews_disabled; 500: internal_error.",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewStartRequestDto"
                  }
                ]
              }
            },
            "text/json": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewStartRequestDto"
                  }
                ]
              }
            },
            "application/*\u002Bjson": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewStartRequestDto"
                  }
                ]
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "OK",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ReviewStartResponseDtoSuccessResponse"
                }
              }
            }
          },
          "400": {
            "description": "Binding/validation failure: extended ValidationProblemDetails, code validation_failed.",
            "content": {
              "application/problem\u002Bjson": {
                "schema": {
                  "required": [
                    "type",
                    "title",
                    "status",
                    "errors",
                    "traceId",
                    "success",
                    "data",
                    "error",
                    "message",
                    "code"
                  ],
                  "type": "object",
                  "properties": {
                    "type": {
                      "type": "string"
                    },
                    "title": {
                      "type": "string"
                    },
                    "status": {
                      "enum": [
                        400
                      ],
                      "type": "integer"
                    },
                    "detail": {
                      "type": "string",
                      "nullable": true
                    },
                    "instance": {
                      "type": "string",
                      "nullable": true
                    },
                    "errors": {
                      "type": "object",
                      "additionalProperties": {
                        "type": "array",
                        "items": {
                          "type": "string"
                        }
                      }
                    },
                    "traceId": {
                      "type": "string"
                    },
                    "success": {
                      "enum": [
                        false
                      ],
                      "type": "boolean"
                    },
                    "data": {
                      "enum": [
                        null
                      ],
                      "type": "object",
                      "nullable": true
                    },
                    "error": {
                      "type": "string"
                    },
                    "message": {
                      "type": "string"
                    },
                    "code": {
                      "enum": [
                        "validation_failed"
                      ],
                      "type": "string"
                    }
                  },
                  "description": "ValidationProblemDetails with sanitized field errors and R7 compatibility extensions. Optional standard ProblemDetails members may also occur."
                }
              }
            }
          },
          "401": {
            "description": "Framework Bearer challenge may have an empty body. If the action rejects an authenticated principal, application/json ApiErrorResponse is returned; see the documented codes.",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "404": {
            "description": "Not Found Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "409": {
            "description": "Conflict Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "503": {
            "description": "Service Unavailable Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "500": {
            "description": "Internal Server Error Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          }
        },
        "security": [
          {
            "Bearer": []
          }
        ]
      }
    },
    "/api/reviews/submit": {
      "post": {
        "tags": [
          "Reviews"
        ],
        "description": " Stable application errors: 400: invalid_review_answer, validation_failed; 401: invalid_token; 404: review_session_unavailable; 409: review_session_expired, review_state_changed, review_submission_conflict; 503: reviews_disabled; 500: internal_error.",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewSubmitRequestDto"
                  }
                ]
              }
            },
            "text/json": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewSubmitRequestDto"
                  }
                ]
              }
            },
            "application/*\u002Bjson": {
              "schema": {
                "allOf": [
                  {
                    "$ref": "#/components/schemas/ReviewSubmitRequestDto"
                  }
                ]
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "OK",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ReviewSubmitResponseDtoSuccessResponse"
                }
              }
            }
          },
          "400": {
            "description": "Either application/json ApiErrorResponse for business rejection, or application/problem\u002Bjson extended ValidationProblemDetails for binding/validation (validation_failed).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              },
              "application/problem\u002Bjson": {
                "schema": {
                  "required": [
                    "type",
                    "title",
                    "status",
                    "errors",
                    "traceId",
                    "success",
                    "data",
                    "error",
                    "message",
                    "code"
                  ],
                  "type": "object",
                  "properties": {
                    "type": {
                      "type": "string"
                    },
                    "title": {
                      "type": "string"
                    },
                    "status": {
                      "enum": [
                        400
                      ],
                      "type": "integer"
                    },
                    "detail": {
                      "type": "string",
                      "nullable": true
                    },
                    "instance": {
                      "type": "string",
                      "nullable": true
                    },
                    "errors": {
                      "type": "object",
                      "additionalProperties": {
                        "type": "array",
                        "items": {
                          "type": "string"
                        }
                      }
                    },
                    "traceId": {
                      "type": "string"
                    },
                    "success": {
                      "enum": [
                        false
                      ],
                      "type": "boolean"
                    },
                    "data": {
                      "enum": [
                        null
                      ],
                      "type": "object",
                      "nullable": true
                    },
                    "error": {
                      "type": "string"
                    },
                    "message": {
                      "type": "string"
                    },
                    "code": {
                      "enum": [
                        "validation_failed"
                      ],
                      "type": "string"
                    }
                  },
                  "description": "ValidationProblemDetails with sanitized field errors and R7 compatibility extensions. Optional standard ProblemDetails members may also occur."
                }
              }
            }
          },
          "401": {
            "description": "Framework Bearer challenge may have an empty body. If the action rejects an authenticated principal, application/json ApiErrorResponse is returned; see the documented codes.",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "404": {
            "description": "Not Found Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "409": {
            "description": "Conflict Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "503": {
            "description": "Service Unavailable Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          },
          "500": {
            "description": "Internal Server Error Application-classified failures use the six-field ApiErrorResponse (application/json).",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/ApiErrorResponse"
                }
              }
            }
          }
        },
        "security": [
          {
            "Bearer": []
          }
        ]
      }
    }
  }
};
