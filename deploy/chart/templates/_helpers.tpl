{{- define "storefront.labels" -}}
app.kubernetes.io/part-of: storefront
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/version: {{ .Values.image.tag | quote }}
{{- end }}

{{- define "storefront.image" -}}
{{ .root.Values.image.registry }}/{{ .name }}:{{ required "image.tag is required (the commit SHA)" .root.Values.image.tag }}
{{- end }}

{{/*
The connection string, built from CNPG's secret. Kubernetes expands $(VAR) from variables
defined earlier in the same list, so the password never appears in the manifest.
*/}}
{{- define "storefront.databaseEnv" -}}
- name: DB_HOST
  valueFrom: { secretKeyRef: { name: {{ .Values.database.secretName }}, key: host } }
- name: DB_PORT
  valueFrom: { secretKeyRef: { name: {{ .Values.database.secretName }}, key: port } }
- name: DB_NAME
  valueFrom: { secretKeyRef: { name: {{ .Values.database.secretName }}, key: dbname } }
- name: DB_USER
  valueFrom: { secretKeyRef: { name: {{ .Values.database.secretName }}, key: username } }
- name: DB_PASSWORD
  valueFrom: { secretKeyRef: { name: {{ .Values.database.secretName }}, key: password } }
- name: ConnectionStrings__Storefront
  value: Host=$(DB_HOST);Port=$(DB_PORT);Database=$(DB_NAME);Username=$(DB_USER);Password=$(DB_PASSWORD)
{{- end }}

{{- define "storefront.apiSecurityContext" -}}
allowPrivilegeEscalation: false
runAsNonRoot: true
capabilities:
  drop: [ALL]
{{- end }}

{{/*
Pins a pod to the storefront's own node pool. The pool's nodes carry the label and a NoSchedule taint,
so only pods that tolerate it land there, and these pods land nowhere else.
*/}}
{{- define "storefront.scheduling" -}}
{{- with .Values.dedicatedNode -}}
nodeSelector:
  {{ .key }}: {{ .value }}
tolerations:
  - key: {{ .taintKey }}
    operator: Equal
    value: {{ .value }}
    effect: NoSchedule
{{- end }}
{{- end }}
