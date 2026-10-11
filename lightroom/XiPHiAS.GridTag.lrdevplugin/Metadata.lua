return {
    schemaVersion = 1,
    metadataFieldsForPhotos = {
        { title = "Status", id = "status", dataType = "enum", searchable = true,
          values = {
              { value = "auto", title = "Automatisch" },
              { value = "review", title = "Controleren" },
              { value = "manual", title = "Handmatig" },
              { value = "noCar", title = "Geen auto" },
              { value = "error", title = "Fout" },
          } },
        { title = "Startnummer", id = "number", dataType = "string", searchable = true },
        { title = "Startnummer (handmatig)", id = "manualNumber", dataType = "string", searchable = true },
        -- The SDK has no double dataType; omit it to allow numeric confidence values.
        { title = "Vertrouwen", id = "confidence", searchable = true },
        { title = "Redenen", id = "reasons", dataType = "string", searchable = true },
        { title = "Sessie", id = "session", dataType = "string", searchable = true },
        { title = "XiPHiAS GridTag keywords", id = "keywords", dataType = "string", searchable = true },
        { title = "Toolversie", id = "toolVersion", dataType = "string", searchable = true },
    },
}
