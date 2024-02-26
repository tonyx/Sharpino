Those templates may help filling in the sql script files created using the dbmate tool.
You can just the placeholders with the actual values that are the version/storagename of the contexts and version/storagename of the aggregates.  
(use Unix sed tool for instance).

Aggregates are slightly different from contexts, as they have a guid and a common event table and a separate aggregate events table.