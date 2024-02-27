Those templates may help filling in the sql script files created using the dbmate tool.
You can just substitute the placeholders with the actual values that are the version/storageName of the contexts and version/storageName of the aggregates.  
(using Unix sed tool for instance).

Aggregates are slightly different from contexts, as they have a guid and a common event table and a separate aggregate events table.
